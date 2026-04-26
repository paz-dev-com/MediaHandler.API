using MediaHandler.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<Domain.Entities.Media> Medias { get; }
    DbSet<MediaFile> MediaFiles { get; }
    DbSet<MediaGenre> MediaGenres { get; }
    DbSet<UserMedia> UserMedias { get; }
    DbSet<WishlistItem> WishlistItems { get; }
    DbSet<TvSeason> TvSeasons { get; }
    DbSet<TvEpisode> TvEpisodes { get; }
    DbSet<UserEpisode> UserEpisodes { get; }

    // ── Scanner entities ─────────────────────────────────────────────────────
    DbSet<LibraryRoot> LibraryRoots { get; }
    DbSet<ScanRun> ScanRuns { get; }
    DbSet<ScanItemDecision> ScanItemDecisions { get; }
    DbSet<ReviewItem> ReviewItems { get; }
    DbSet<ExclusionRule> ExclusionRules { get; }
    DbSet<StackGroup> StackGroups { get; }
    DbSet<NfoMetadata> NfoMetadata { get; }
    DbSet<EpisodeFileLink> EpisodeFileLinks { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
