using System.Net.Http.Headers;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Infrastructure.Nas;
using MediaHandler.Infrastructure.Options;
using MediaHandler.Infrastructure.Persistence;
using MediaHandler.Infrastructure.Tmdb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
}
