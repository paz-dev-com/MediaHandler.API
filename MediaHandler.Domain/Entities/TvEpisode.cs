using MediaHandler.Domain.Common;

namespace MediaHandler.Domain.Entities;

public class TvEpisode : BaseEntity
{
    public required Guid SeasonId { get; set; }
    public required int EpisodeNumber { get; set; }
    public required string Name { get; set; }
    public string? Overview { get; set; }
    public DateTime? AirDate { get; set; }
    public string? StillPath { get; set; }
    public int? Runtime { get; set; }

    // ── Scanner additions ────────────────────────────────────────────────────

    /// <summary>
    ///     All physical files that carry this episode (many-to-many via <see cref="EpisodeFileLink" />).
    ///     Replaces the previous direct <c>MediaFileId</c> FK to support multi-episode files.
    /// </summary>
    public ICollection<EpisodeFileLink> EpisodeFileLinks { get; set; } = [];

    /// <summary>
    ///     Convenience resolver that returns the primary (first, <c>OrderInFile = 1</c>) file
    ///     for this episode, or <c>null</c> if no file has been linked yet.
    /// </summary>
    public MediaFile? PrimaryFile =>
        EpisodeFileLinks
            .OrderBy(l => l.OrderInFile)
            .FirstOrDefault()
            ?.MediaFile;

    // ── Navigation ──────────────────────────────────────────────────────────

    public TvSeason Season { get; set; } = null!;
    public ICollection<UserEpisode> UserEpisodes { get; set; } = new List<UserEpisode>();
}