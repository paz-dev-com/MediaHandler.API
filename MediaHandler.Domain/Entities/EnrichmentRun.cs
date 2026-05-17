using MediaHandler.Domain.Common;
using MediaHandler.Domain.Enums;

namespace MediaHandler.Domain.Entities;

/// <summary>
///     Persisted record of a single batch TMDB enrichment run.
///     Enforces a concurrency invariant: at most one row with
///     <see cref="Status" /> = <see cref="EnrichmentStatus.Running" /> at any time
///     (backed by a filtered unique index on the database).
/// </summary>
public class EnrichmentRun : BaseEntity
{
    /// <summary>Current lifecycle state of the enrichment run.</summary>
    public required EnrichmentStatus Status { get; set; }

    /// <summary>
    ///     Reason for failure; populated when <see cref="Status" /> transitions to
    ///     <see cref="EnrichmentStatus.Failed" /> (including crash-recovery restarts).
    /// </summary>
    public string? FailureReason { get; set; }

    /// <summary>UTC timestamp when the coordinator began processing.</summary>
    public required DateTime StartedAt { get; set; }

    /// <summary>UTC timestamp when the run reached a terminal state (<c>null</c> while in progress).</summary>
    public DateTime? FinishedAt { get; set; }

    /// <summary>Total number of media entries selected for enrichment.</summary>
    public required int TotalItems { get; set; }

    /// <summary>Number of entries successfully enriched so far.</summary>
    public int EnrichedCount { get; set; }

    /// <summary>Number of entries that failed enrichment so far.</summary>
    public int FailedCount { get; set; }

    /// <summary>Number of entries skipped (already enriched and unchanged).</summary>
    public int SkippedCount { get; set; }

    /// <summary>
    ///     Human-readable identifier of the entry currently being processed
    ///     (e.g., TMDB ID or title). Updated by the coordinator every ~10 items or ~5 seconds.
    /// </summary>
    public string? CurrentItem { get; set; }

    /// <summary>
    ///     JSON array of per-entry error details.
    ///     Stored as <c>nvarchar(max)</c>; deserialized only when needed for reporting.
    /// </summary>
    public string? ErrorDetailsJson { get; set; }

    /// <summary>
    ///     JSON array tracking per-media processing results for this run.
    ///     Each element is <c>{ "MediaId": "guid", "Status": "Enriched|Failed|Skipped" }</c>.
    ///     Stored as <c>nvarchar(max)</c>; used by the enrichment details endpoint.
    /// </summary>
    public string? EnrichedMediaIdsJson { get; set; }
}
