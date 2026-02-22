using MediaHandler.Domain.Interfaces;
using MediaHandler.Infrastructure.Identity;
using MediaHandler.Infrastructure.Nas;
using MediaHandler.Infrastructure.Options;
using MediaHandler.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MediaHandler.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<MediaHandlerDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"),
                b => b.MigrationsAssembly(typeof(MediaHandlerDbContext).Assembly.FullName)));

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

        services.AddScoped<ICurrentUserService, CurrentUserService>();

        services.AddHttpClient("Freebox")
            .ConfigureHttpClient((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<NasOptions>>().Value;
                client.BaseAddress = new Uri(options.FreeboxUrl);
            });

        services.AddSingleton<INasService, FreeboxNasService>();

        return services;
    }
}
