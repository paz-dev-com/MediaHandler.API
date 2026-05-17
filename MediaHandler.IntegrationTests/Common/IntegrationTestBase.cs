using AutoMapper;
using MediaHandler.Application.Common.Mappings;
using MediaHandler.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

namespace MediaHandler.IntegrationTests.Common;

public abstract class IntegrationTestBase : IAsyncLifetime
{
    private readonly MsSqlContainer _db = new MsSqlBuilder().WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    private ServiceProvider? _serviceProvider;

    protected MediaHandlerDbContext DbContext { get; private set; } = null!;
    protected IMapper Mapper { get; private set; } = null!;

    /// <summary>
    ///     EF Core options for the test SQL Server container.
    ///     Expose so scanner tests can create additional independent DbContext instances
    ///     for background workers without sharing the polling context.
    /// </summary>
    protected DbContextOptions<MediaHandlerDbContext> DbContextOptions { get; private set; } = null!;

    public virtual async ValueTask InitializeAsync()
    {
        await _db.StartAsync();

        DbContextOptions = new DbContextOptionsBuilder<MediaHandlerDbContext>()
            .UseSqlServer(
                _db.GetConnectionString(),
                b => b.MigrationsAssembly(typeof(MediaHandlerDbContext).Assembly.FullName))
            .Options;

        DbContext = new MediaHandlerDbContext(DbContextOptions);
        await DbContext.Database.MigrateAsync();

        // Use AddMaps to match the production DI setup — required for ProjectTo<T> to work
        // correctly against a real SQL Server provider via EF Core.
        _serviceProvider = new ServiceCollection()
            .AddLogging()
            .AddAutoMapper(cfg => cfg.AddMaps(typeof(UserMappingProfile).Assembly))
            .BuildServiceProvider();

        Mapper = _serviceProvider.GetRequiredService<IMapper>();
    }

    public virtual async ValueTask DisposeAsync()
    {
        await DbContext.DisposeAsync();
        await _db.DisposeAsync();
        if (_serviceProvider is not null)
            await _serviceProvider.DisposeAsync();
    }
}