namespace MediaHandler.Domain.Enums;

/// <summary>
/// Outcome that the scanner pipeline assigned to a single file path during a <c>ScanRun</c>.
/// Every processed path produces exactly one <c>ScanItemDecision</c> row carrying this kind.
/// </summary>
public enum ScanDecisionKind
{
    /// <summary>File is new — a <c>MediaFile</c> row was inserted.</summary>
    Added,

    /// <summary>File already existed but its fingerprint changed — the row was updated.</summary>
    Updated,

    /// <summary>File already existed and its fingerprint is unchanged — no DB write needed.</summary>
    Unchanged,

    /// <summary>File was present in a previous scan but is no longer visible on the NAS.</summary>
    Removed,

    /// <summary>File was deliberately skipped because an <c>ExclusionRule</c> matched.</summary>
    Excluded,

    /// <summary>
    /// File could not be fully resolved (TMDB miss, ambiguous match, unparseable name …)
    /// and an open <c>ReviewItem</c> was created for admin attention.
    /// </summary>
    NeedsReview
}

