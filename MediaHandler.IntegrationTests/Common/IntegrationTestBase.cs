using AutoMapper;
using MediaHandler.Application.Common.Mappings;
using MediaHandler.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace MediaHandler.IntegrationTests.Common;

public abstract class IntegrationTestBase : IAsyncLifetime
{
    private readonly MsSqlContainer _db = new MsSqlBuilder().Build();

    protected MediaHandlerDbContext DbContext { get; private set; } = null!;
    protected IMapper Mapper { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _db.StartAsync();

        var options = new DbContextOptionsBuilder<MediaHandlerDbContext>()
            .UseSqlServer(_db.GetConnectionString())
            .Options;

        DbContext = new MediaHandlerDbContext(options);
        await DbContext.Database.MigrateAsync();

        Mapper = new MapperConfiguration(cfg =>
        {
            cfg.AddProfile<UserMappingProfile>();
            cfg.AddProfile<WishlistMappingProfile>();
        }).CreateMapper();
    }

    public async Task DisposeAsync()
    {
        await DbContext.DisposeAsync();
        await _db.DisposeAsync();
    }
}
