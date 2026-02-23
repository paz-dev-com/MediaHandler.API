using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Tests.Common;

public class TestDbContext : DbContext, IApplicationDbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Domain.Entities.Media> Medias => Set<Domain.Entities.Media>();
    public DbSet<MediaFile> MediaFiles => Set<MediaFile>();
    public DbSet<MediaGenre> MediaGenres => Set<MediaGenre>();
    public DbSet<UserMedia> UserMedias => Set<UserMedia>();
    public DbSet<WishlistItem> WishlistItems => Set<WishlistItem>();
    public DbSet<TvSeason> TvSeasons => Set<TvSeason>();
    public DbSet<TvEpisode> TvEpisodes => Set<TvEpisode>();
    public DbSet<UserEpisode> UserEpisodes => Set<UserEpisode>();

    public static TestDbContext Create()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContext(options);
    }
}
