using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Infrastructure.Persistence;

public class MediaHandlerDbContext : DbContext, IApplicationDbContext
{
    public MediaHandlerDbContext(DbContextOptions<MediaHandlerDbContext> options) : base(options)
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MediaHandlerDbContext).Assembly);
    }
}
