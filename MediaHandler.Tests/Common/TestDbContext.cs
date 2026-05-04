using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Tests.Common;

public class TestDbContext : DbContext, IApplicationDbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Media> Medias => Set<Media>();
    public DbSet<MediaFile> MediaFiles => Set<MediaFile>();
    public DbSet<MediaGenre> MediaGenres => Set<MediaGenre>();
    public DbSet<UserMedia> UserMedias => Set<UserMedia>();
    public DbSet<WishlistItem> WishlistItems => Set<WishlistItem>();
    public DbSet<TvSeason> TvSeasons => Set<TvSeason>();
    public DbSet<TvEpisode> TvEpisodes => Set<TvEpisode>();
    public DbSet<UserEpisode> UserEpisodes => Set<UserEpisode>();

    // Scanner entities
    public DbSet<LibraryRoot> LibraryRoots => Set<LibraryRoot>();
    public DbSet<ScanRun> ScanRuns => Set<ScanRun>();
    public DbSet<ScanItemDecision> ScanItemDecisions => Set<ScanItemDecision>();
    public DbSet<ReviewItem> ReviewItems => Set<ReviewItem>();
    public DbSet<ExclusionRule> ExclusionRules => Set<ExclusionRule>();
    public DbSet<StackGroup> StackGroups => Set<StackGroup>();
    public DbSet<NfoMetadata> NfoMetadata => Set<NfoMetadata>();
    public DbSet<EpisodeFileLink> EpisodeFileLinks => Set<EpisodeFileLink>();
    public DbSet<EnrichmentRun> EnrichmentRuns => Set<EnrichmentRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // MediaGenre uses a composite PK (MediaId, Name) that EF Core InMemory
        // cannot infer by convention — must be configured explicitly.
        modelBuilder.Entity<MediaGenre>().HasKey(g => new { g.MediaId, g.Name });
    }

    public static TestDbContext Create()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContext(options);
    }
}