using MediaHandler.Domain.Common;
using MediaHandler.Domain.Enums;

namespace MediaHandler.Domain.Entities;

public class Media : BaseEntity
{
    public required int TmdbId { get; set; }
    public required string Title { get; set; }
    public string? OriginalTitle { get; set; }
    public string? Overview { get; set; }
    public MediaType Type { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public int? Runtime { get; set; }
    public string? PosterPath { get; set; }
    public string? BackdropPath { get; set; }
    public decimal? VoteAverage { get; set; }
    public int? VoteCount { get; set; }
    public string? Language { get; set; }

    // ── Scanner additions (T021) ─────────────────────────────────────────────

    /// <summary>
    /// Release year parsed from the filename or NFO, distinct from
    /// <see cref="ReleaseDate"/> which is populated from TMDB.
    /// </summary>
    public int? Year { get; set; }

    /// <summary>
    /// FK to the <see cref="NfoMetadata"/> row that drove identity for this item,
    /// <c>null</c> when identity was resolved from the filename alone.
    /// </summary>
    public Guid? NfoMetadataId { get; set; }

    /// <summary>Parsed NFO sidecar, if one existed for this media item.</summary>
    public NfoMetadata? NfoMetadata { get; set; }

    /// <summary>
    /// Multi-part stack descriptor for stacked movies; <c>null</c> for single-file items.
    /// </summary>
    public StackGroup? StackGroup { get; set; }

    // ── Navigation ──────────────────────────────────────────────────────────

    public ICollection<MediaGenre> Genres { get; set; } = new List<MediaGenre>();
    public ICollection<MediaFile> MediaFiles { get; set; } = new List<MediaFile>();
    public ICollection<UserMedia> UserMedias { get; set; } = new List<UserMedia>();
    public ICollection<TvSeason> TvSeasons { get; set; } = new List<TvSeason>();
}
