using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace MediaHandler.Infrastructure.Persistence;

public class MediaHandlerDbContextFactory : IDesignTimeDbContextFactory<MediaHandlerDbContext>
{
    public MediaHandlerDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "..", "MediaHandler.API"))
            .AddJsonFile("appsettings.json", true)
            .AddJsonFile("appsettings.Development.json", true)
            .Build();

        var optionsBuilder = new DbContextOptionsBuilder<MediaHandlerDbContext>();
        optionsBuilder.UseSqlServer(
            configuration.GetConnectionString("DefaultConnection"),
            b => b.MigrationsAssembly(typeof(MediaHandlerDbContext).Assembly.FullName));

        return new MediaHandlerDbContext(optionsBuilder.Options);
    }
}