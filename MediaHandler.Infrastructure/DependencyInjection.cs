using System.Net.Http.Headers;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Domain.Enums;
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

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<MediaHandlerDbContext>());

        services.AddHttpClient("Freebox")
            .ConfigureHttpClient((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<NasOptions>>().Value;
                client.BaseAddress = new Uri(options.FreeboxUrl);
            })
            .AddStandardResilienceHandler();

        services.AddScoped<INasService, FreeboxNasService>();
        services.AddScoped<IMediaFileNameParser, MediaFileNameParser>();
        services.AddScoped<IMediaImportService, MediaImportService>();
        services.AddScoped<IMediaAutoMatchService, MediaAutoMatchService>();

        // ── Scanner services ─────────────────────────────────────────────────
        // Singleton: owns in-memory scan state across requests
        services.AddSingleton<ScanRunCoordinator>();
        services.AddSingleton<IScanRunCoordinator>(sp => sp.GetRequiredService<ScanRunCoordinator>());

        // Scoped scanner services (infrastructure-side)
        services.AddScoped<INasFileEnumerator, NasFileEnumerator>();
        services.AddScoped<IKodiNameParser, KodiNameParser>();
        services.AddScoped<IExclusionEvaluator, ExclusionEvaluator>();
        services.AddScoped<IStackingDetector, StackingDetector>();
        services.AddScoped<ITvEpisodeMatcher, TvEpisodeMatcher>();

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
    /// On application startup, transitions any <c>ScanRun</c> rows left in <c>Running</c>
    /// status to <c>Failed</c> with a standard failure reason.
    /// Call this from <c>Program.cs</c> after the app is built but before it starts accepting requests.
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
}
