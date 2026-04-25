namespace MediaHandler.Domain.Enums;

/// <summary>
/// Describes why a file ended up in the review queue during a <c>ScanRun</c>.
/// </summary>
public enum ReviewReason
{
    /// <summary>TMDB returned zero results for the parsed title (and year, if available).</summary>
    NoTmdbResult,

    /// <summary>TMDB returned ≥ 2 candidates with a similar popularity score; unable to pick one.</summary>
    MultipleCandidates,

    /// <summary>
    /// A single TMDB result was found but its release year differs from the parsed year
    /// by more than ±1 year.
    /// </summary>
    YearMismatch,

    /// <summary>Season/episode numbers could not be extracted from the filename.</summary>
    UnparseableEpisode,

    /// <summary>An NFO sidecar was found but could not be parsed as valid XML.</summary>
    NfoMalformed,

    /// <summary>
    /// The file extension is not in the video allowlist and no exclusion rule matched,
    /// leaving the file in an indeterminate state.
    /// </summary>
    UnknownFormat,

    /// <summary>
    /// A previously matched <c>MediaFile</c> was flagged missing, and the corresponding
    /// <c>ReviewItem</c> was kept open past the grace period.
    /// </summary>
    OrphanedAfterMissing
}

