using MediaHandler.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<Domain.Entities.Media> Medias { get; }
    DbSet<MediaFile> MediaFiles { get; }
    DbSet<UserMedia> UserMedias { get; }
    DbSet<WishlistItem> WishlistItems { get; }
    DbSet<TvSeason> TvSeasons { get; }
    DbSet<TvEpisode> TvEpisodes { get; }
    DbSet<UserEpisode> UserEpisodes { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
