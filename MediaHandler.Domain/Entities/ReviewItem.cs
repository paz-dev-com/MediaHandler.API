using MediaHandler.Domain.Common;
using MediaHandler.Domain.Enums;

namespace MediaHandler.Domain.Entities;

/// <summary>
///     Represents a file that the scanner could not fully resolve automatically and
///     requires administrator intervention.
/// </summary>
/// <remarks>
///     <para>
///         Review-item state (<see cref="ReviewStatus" />) lives here rather than on
///         <see cref="Media" /> because an unmatched file does not yet have a <c>Media</c>
///         row — it only has a filesystem path.
///     </para>
///     <para>
///         A unique partial index on <c>(FilePath, Status = 'Open')</c> prevents duplicate
///         open items for the same path.
///     </para>
/// </remarks>
public class ReviewItem : BaseEntity
{
    /// <summary>Absolute NAS path of the file that needs attention.</summary>
    public required string FilePath { get; set; }

    /// <summary>Root cause of the review flag.</summary>
    public required ReviewReason Reason { get; set; }

    /// <summary>Current resolution state.</summary>
    public required ReviewStatus Status { get; set; }

    /// <summary>Which subsystem surfaced this item (scanner or Kodi database import).</summary>
    public ReviewItemSource Source { get; set; } = ReviewItemSource.Scan;

    // Parsed metadata (populated from the filename / NFO)

    /// <summary>Title extracted from the filename or NFO; may be <c>null</c> if unparseable.</summary>
    public string? ParsedTitle { get; set; }

    /// <summary>Year extracted from the filename or NFO.</summary>
    public int? ParsedYear { get; set; }

    /// <summary>Season number extracted from the filename (TV shows only).</summary>
    public int? ParsedSeason { get; set; }

    /// <summary>Episode number extracted from the filename (TV shows only).</summary>
    public int? ParsedEpisode { get; set; }

    // TMDB candidates

    /// <summary>
    ///     JSON-serialised array of TMDB candidates:
    ///     <c>[{"tmdbId":int,"kind":"Film|TvShow","title":string,"year":int,"score":float}]</c>.
    /// </summary>
    public string CandidatesJson { get; set; } = "[]";

    // Resolution fields

    /// <summary>TMDB id chosen by the administrator when <see cref="ReviewResolutionAction.Assign" />.</summary>
    public int? ResolvedTmdbId { get; set; }

    /// <summary>Media kind decided by the administrator at resolution time.</summary>
    public MediaType? ResolvedKind { get; set; }

    /// <summary>UTC timestamp at which an administrator resolved this item.</summary>
    public DateTime? ResolvedAt { get; set; }

    /// <summary>User id (subject claim) of the administrator who resolved this item.</summary>
    public string? ResolvedBy { get; set; }

    /// <summary>
    ///     The first <see cref="ScanRun" /> that surfaced this item.
    ///     Allows grouping review items by scan run in diagnostic reports.
    /// </summary>
    public Guid? FirstSeenScanRunId { get; set; }
}