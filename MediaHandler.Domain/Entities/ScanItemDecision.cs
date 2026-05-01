using MediaHandler.Domain.Common;
using MediaHandler.Domain.Enums;

namespace MediaHandler.Domain.Entities;

/// <summary>
///     Audit record capturing the scanner pipeline's decision for a single file path
///     within a <see cref="ScanRun" />.
///     Every path the pipeline touches — including excluded and pending-review paths —
///     produces exactly one row so that an administrator can answer
///     "why was <em>this</em> file ignored?" in under 30 seconds (SC-006).
/// </summary>
public class ScanItemDecision : BaseEntity
{
    /// <summary>The run that produced this decision.</summary>
    public required Guid ScanRunId { get; set; }

    /// <summary>Absolute NAS path of the file the pipeline evaluated.</summary>
    public required string FilePath { get; set; }

    /// <summary>Outcome assigned by the pipeline.</summary>
    public required ScanDecisionKind Kind { get; set; }

    /// <summary>
    ///     Human-readable explanation; populated for <see cref="ScanDecisionKind.Excluded" />
    ///     and <see cref="ScanDecisionKind.NeedsReview" />.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    ///     Identifier of the <see cref="ExclusionRule" /> that caused an exclusion decision.
    ///     <c>null</c> for non-exclusion outcomes.
    /// </summary>
    public string? RuleId { get; set; }

    /// <summary>
    ///     The <c>MediaFile</c> row created or updated by this decision, if applicable.
    /// </summary>
    public Guid? MediaFileId { get; set; }

    /// <summary>
    ///     The open <see cref="ReviewItem" /> created for this path, if
    ///     <see cref="Kind" /> is <see cref="ScanDecisionKind.NeedsReview" />.
    /// </summary>
    public Guid? ReviewItemId { get; set; }

    // ── Navigation ──────────────────────────────────────────────────────────

    public ScanRun ScanRun { get; set; } = null!;
    public MediaFile? MediaFile { get; set; }
    public ReviewItem? ReviewItem { get; set; }
}