using MediaHandler.Domain.Common;
using MediaHandler.Domain.Enums;

namespace MediaHandler.Domain.Entities;

/// <summary>
/// Records a single execution of the scanner pipeline.
/// Provides per-run summary counters and a full per-file audit trail via
/// the <see cref="Decisions"/> collection.
/// </summary>
/// <remarks>
/// A filtered unique index on <c>Status = 'Running'</c> prevents concurrent scans
/// (single-active-scan invariant enforced at the database level).
/// </remarks>
public class ScanRun : BaseEntity
{
    /// <summary>Whether this run should re-visit every file or only changed ones.</summary>
    public required ScanMode Mode { get; set; }

    /// <summary>Current lifecycle state of the run.</summary>
    public required ScanStatus Status { get; set; }

    /// <summary>UTC time the run was started. Indexed DESC for history queries.</summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC time the run reached a terminal state, or <c>null</c> while still running.</summary>
    public DateTime? FinishedAt { get; set; }

    /// <summary>
    /// Human-readable description of why the run failed.
    /// Populated when <see cref="Status"/> transitions to <see cref="ScanStatus.Failed"/>.
    /// </summary>
    public string? FailureReason { get; set; }

    /// <summary>
    /// JSON-serialised array of <c>LibraryRoot</c> <see cref="Guid"/> values that were
    /// included in this run.  Denormalised so the record is self-contained after roots
    /// are deleted.
    /// </summary>
    public string LibraryRootIdsJson { get; set; } = "[]";

    // ── Denormalised summary counters (updated in batches by the pipeline) ──

    /// <summary>Total number of paths considered by the pipeline.</summary>
    public int TotalDiscovered { get; set; }

    /// <summary>Files that produced a new <c>MediaFile</c> row.</summary>
    public int Added { get; set; }

    /// <summary>Files whose fingerprint changed and whose existing row was updated.</summary>
    public int Updated { get; set; }

    /// <summary>Files that matched a stored fingerprint — no DB write was needed.</summary>
    public int Unchanged { get; set; }

    /// <summary>Files previously seen but absent from the NAS during this run.</summary>
    public int Removed { get; set; }

    /// <summary>Files intentionally skipped by an <c>ExclusionRule</c>.</summary>
    public int Excluded { get; set; }

    /// <summary>Files routed to the admin review queue.</summary>
    public int NeedsReview { get; set; }

    // ── Navigation ──────────────────────────────────────────────────────────

    /// <summary>Per-file decision rows written by the pipeline (one per processed path).</summary>
    public ICollection<ScanItemDecision> Decisions { get; set; } = [];
}

