using System.Net.Http.Headers;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models.Scanner;
using MediaHandler.Domain.Enums;
using MediaHandler.Infrastructure.Kodi;
using MediaHandler.Infrastructure.Nas;
using MediaHandler.Infrastructure.Nas.Scanner;
using MediaHandler.Infrastructure.Options;
using MediaHandler.Infrastructure.Persistence;
using MediaHandler.Infrastructure.Services;
using MediaHandler.Infrastructure.Tmdb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MediaHandler.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<AuditableEntitySaveChangesInterceptor>();
        services.AddScoped<DomainEventDispatchInterceptor>();
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        services.AddDbContext<MediaHandlerDbContext>((sp, options) =>
        {
            var auditInterceptor = sp.GetRequiredService<AuditableEntitySaveChangesInterceptor>();
            var domainEventInterceptor = sp.GetRequiredService<DomainEventDispatchInterceptor>();
            options.UseSqlServer(
                    configuration.GetConnectionString("DefaultConnection"),
                    b => b.MigrationsAssembly(typeof(MediaHandlerDbContext).Assembly.FullName))
                .AddInterceptors(auditInterceptor, domainEventInterceptor);
        });

        services.AddOptions<OktaOptions>()
            .Bind(configuration.GetSection(OktaOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<TmdbOptions>()
            .Bind(configuration.GetSection(TmdbOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<NasOptions>()
            .Bind(configuration.GetSection(NasOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Scanner options — IOptionsMonitor<ReleaseTagOptions> is available for injection
        // by any service that needs runtime-reloadable release-tag pattern configuration.
        services.AddOptions<ReleaseTagOptions>()
            .Bind(configuration.GetSection(ReleaseTagOptions.SectionName));

        services.AddOptions<KodiImportOptions>()
            .Bind(configuration.GetSection(KodiImportOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<MediaHandlerDbContext>());

        services.AddHttpClient("Freebox")
            .ConfigureHttpClient((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<NasOptions>>().Value;
                client.BaseAddress = new Uri(options.FreeboxUrl);
            })
            .AddStandardResilienceHandler();

        services.AddScoped<INasService, FreeboxNasService>();
#pragma warning disable CS0618 // Intentionally retained for legacy FilesController compatibility
        services.AddScoped<IMediaFileNameParser, MediaFileNameParser>();
#pragma warning restore CS0618
        services.AddScoped<IMediaImportService, MediaImportService>();
        services.AddScoped<IMediaAutoMatchService, MediaAutoMatchService>();

        // Scanner services
        // Singleton: owns in-memory scan state across requests.
        // Uses IServiceScopeFactory internally to resolve scoped dependencies per scan run.
        services.AddSingleton<ScanRunCoordinator>();
        services.AddSingleton<IScanRunCoordinator>(sp => sp.GetRequiredService<ScanRunCoordinator>());

        // Enrichment coordinator
        // Singleton: owns background TMDB enrichment run lifecycle (mirrors ScanRunCoordinator).
        services.AddSingleton<EnrichmentCoordinator>();
        services.AddSingleton<IEnrichmentCoordinator>(sp => sp.GetRequiredService<EnrichmentCoordinator>());

        // Kodi import coordinator
        // Singleton: owns background import run lifecycle (mirrors ScanRunCoordinator).
        services.AddSingleton<ImportRunCoordinator>();
        services.AddSingleton<IImportRunCoordinator>(sp => sp.GetRequiredService<ImportRunCoordinator>());

        // Kodi import scoped services
        services.AddScoped<IKodiVideoDbReader, KodiVideoDbReader>();
        services.AddScoped<IKodiImportFileStore, KodiImportFileStore>();

        // KodiImportPipeline: scoped so each import run gets a fresh pipeline (and fresh DbContext).
        // Resolved by ImportRunCoordinator via IServiceScopeFactory — never injected directly
        // into a singleton.
        services.AddScoped<KodiImportPipeline>();

        // File rename service
        // Scoped: uses IApplicationDbContext (also scoped) for DB updates.
        services.AddScoped<IFileRenameService, FileRenameService>();

        // Scoped scanner services (infrastructure-side)
        services.AddScoped<INasFileEnumerator, NasFileEnumerator>();
        services.AddScoped<IKodiNameParser, KodiNameParser>();
        services.AddScoped<IExclusionEvaluator, ExclusionEvaluator>();
        services.AddScoped<IStackingDetector, StackingDetector>();
        services.AddScoped<ITvEpisodeMatcher, TvEpisodeMatcher>();

        // ScanPipeline: scoped so each scan run gets a fresh pipeline (and fresh DbContext).
        // Resolved by ScanRunCoordinator via IServiceScopeFactory — never injected directly
        // into a singleton.
        services.AddScoped<ScanPipeline>();

        // TmdbMatcher: scoped so its per-scan LRU cache is isolated per request/scan run
        services.AddScoped<ITmdbMatcher, TmdbMatcher>();

        services.AddHttpClient<ITmdbService, TmdbService>()
            .ConfigureHttpClient((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<TmdbOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl);
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", options.ReadAccessToken);
            })
            .AddStandardResilienceHandler();

        return services;
    }

    /// <summary>
    ///     On application startup, transitions any <c>ScanRun</c> rows left in <c>Running</c>
    ///     status to <c>Failed</c> with a standard failure reason.
    ///     Call this from <c>Program.cs</c> after the app is built but before it starts accepting requests.
    /// </summary>
    public static async Task ApplyScanRunRecoveryAsync(IServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaHandlerDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<MediaHandlerDbContext>>();

        var stuckRuns = await db.ScanRuns
            .Where(r => r.Status == ScanStatus.Running)
            .ToListAsync();

        if (stuckRuns.Count == 0)
            return;

        foreach (var run in stuckRuns)
        {
            run.Status = ScanStatus.Failed;
            run.FinishedAt = DateTime.UtcNow;
            run.FailureReason = "Process restarted before scan finished";
        }

        await db.SaveChangesAsync();
        logger.LogWarning(
            "Startup recovery: {Count} scan run(s) were stuck in Running status and have been marked Failed.",
            stuckRuns.Count);
    }

    /// <summary>
    ///     On application startup, transitions any <c>ImportRun</c> rows left in <c>Pending</c> or
    ///     <c>Running</c> status to <c>Failed</c>, deletes their still-referenced uploaded files,
    ///     and purges the Kodi import temp directory (no legitimate uploaded file can exist at
    ///     startup). Call this from <c>Program.cs</c> right after <see cref="ApplyScanRunRecoveryAsync" />.
    /// </summary>
    public static async Task ApplyImportRunRecoveryAsync(IServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaHandlerDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<MediaHandlerDbContext>>();
        var fileStore = scope.ServiceProvider.GetRequiredService<IKodiImportFileStore>();

        var stuckRuns = await db.ImportRuns
            .Where(r => r.Status == ImportRunStatus.Pending || r.Status == ImportRunStatus.Running)
            .ToListAsync();

        foreach (var run in stuckRuns)
        {
            run.Status = ImportRunStatus.Failed;
            run.FinishedAt = DateTime.UtcNow;
            run.FailureReason = "Process restarted before import finished";

            fileStore.Delete(run.UploadedFilePath);
            run.UploadedFilePath = null;
        }

        if (stuckRuns.Count > 0)
        {
            await db.SaveChangesAsync();
            logger.LogWarning(
                "Startup recovery: {Count} import run(s) were stuck and have been marked Failed.",
                stuckRuns.Count);
        }

        fileStore.PurgeOrphans();
    }
}