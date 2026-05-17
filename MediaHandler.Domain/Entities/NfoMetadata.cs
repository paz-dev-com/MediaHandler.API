using MediaHandler.Domain.Common;

namespace MediaHandler.Domain.Entities;

/// <summary>
///     Snapshot of a parsed NFO sidecar file attached to a <see cref="Media" /> row.
///     Captured for diagnostics and to enable the NFO-override precedence chain
///     (<c>NfoTmdbId → ExplicitTokenId → Title+Year → Title</c>).
/// </summary>
/// <remarks>
///     A unique index on <see cref="SourcePath" /> prevents duplicate rows for the
///     same <c>.nfo</c> file across incremental scans.
/// </remarks>
public class NfoMetadata : BaseEntity
{
    /// <summary>Absolute NAS path of the <c>.nfo</c> file that was parsed.</summary>
    public required string SourcePath { get; set; }

    /// <summary>
    ///     Raw XML content of the NFO, truncated to 32 KB for diagnostics.
    ///     Not used for query filtering.
    /// </summary>
    public required string RawContent { get; set; }

    /// <summary>Title extracted from <c>&lt;title&gt;</c>, if present.</summary>
    public string? Title { get; set; }

    /// <summary>Year extracted from <c>&lt;year&gt;</c>, if present.</summary>
    public int? Year { get; set; }

    /// <summary>TMDB id extracted from <c>&lt;tmdbid&gt;</c>, if present.</summary>
    public int? TmdbId { get; set; }

    /// <summary>IMDB id extracted from <c>&lt;id&gt;</c> or <c>&lt;imdbid&gt;</c>, if present.</summary>
    public string? ImdbId { get; set; }

    /// <summary>Season number from a per-episode NFO, if present.</summary>
    public int? Season { get; set; }

    /// <summary>Episode number from a per-episode NFO, if present.</summary>
    public int? Episode { get; set; }

    /// <summary><c>true</c> when the NFO file could not be parsed as valid XML.</summary>
    public bool ParseFailed { get; set; }

    /// <summary>Parse error message; set when <see cref="ParseFailed" /> is <c>true</c>.</summary>
    public string? ParseError { get; set; }
}