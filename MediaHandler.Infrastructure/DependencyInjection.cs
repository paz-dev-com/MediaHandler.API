using MediaHandler.Domain.Interfaces;
using MediaHandler.Infrastructure.Identity;
using MediaHandler.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MediaHandler.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<MediaHandlerDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"),
                b => b.MigrationsAssembly(typeof(MediaHandlerDbContext).Assembly.FullName)));

        services.AddScoped<ICurrentUserService, CurrentUserService>();

        return services;
    }
}
