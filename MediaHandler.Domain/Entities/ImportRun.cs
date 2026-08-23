using MediaHandler.Domain.Common;
using MediaHandler.Domain.Enums;

namespace MediaHandler.Domain.Entities;

/// <summary>
///     Records a single execution of the Kodi video-database import (real run or preview).
///     Provides per-run summary counters and a full per-item audit trail via the
///     <see cref="Outcomes" /> collection.
/// </summary>
/// <remarks>
///     A filtered unique index on <c>Status = 'Running'</c> prevents concurrent imports
///     (single-active-import invariant enforced at the database level).
/// </remarks>
public class ImportRun : BaseEntity
{
    /// <summary>Whether this run persists domain changes or only projects them.</summary>
    public required KodiImportMode Mode { get; set; }

    /// <summary>Current lifecycle state of the run.</summary>
    public required ImportRunStatus Status { get; set; }

    /// <summary>Original name of the uploaded file (e.g. <c>MyVideos121.db</c>); kept for audit.</summary>
    public required string SourceFileName { get; set; }

    /// <summary>Kodi database schema version parsed from the file name suffix.</summary>
    public required int SchemaVersion { get; set; }

    /// <summary>
    ///     Temporary path of the uploaded file while the run needs it.
    ///     Nulled when the file is discarded as the run reaches a terminal state.
    /// </summary>
    public string? UploadedFilePath { get; set; }

    /// <summary>
    ///     JSON-serialised snapshot of the effective ordered path mappings used by this run,
    ///     so the run report remains self-contained after mappings are edited or deleted.
    /// </summary>
    public string PathMappingsJson { get; set; } = "[]";

    /// <summary>
    ///     JSON-serialised array of distinct uncovered Kodi directory prefixes encountered
    ///     during the run (max 100 entries) — the actionable list for extending mappings.
    /// </summary>
    public string UnmatchedPrefixesJson { get; set; } = "[]";

    /// <summary>UTC time the run was started. Indexed for history queries.</summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC time the run reached a terminal state, or <c>null</c> while still running.</summary>
    public DateTime? FinishedAt { get; set; }

    /// <summary>
    ///     Human-readable description of why the run failed.
    ///     Populated when <see cref="Status" /> transitions to <see cref="ImportRunStatus.Failed" />.
    /// </summary>
    public string? FailureReason { get; set; }

    // Denormalised summary counters (updated in batches by the pipeline)

    /// <summary>Total number of Kodi library items considered (excludes <c>NoLongerInKodi</c> rows).</summary>
    public int TotalItems { get; set; }

    /// <summary>New <c>Media</c> rows of kind <c>Film</c> created by this run.</summary>
    public int MoviesCreated { get; set; }

    /// <summary>New <c>Media</c> rows of kind <c>TvShow</c> created by this run.</summary>
    public int ShowsCreated { get; set; }

    /// <summary>New <c>TvEpisode</c> rows created by this run.</summary>
    public int EpisodesCreated { get; set; }

    /// <summary>Kodi items associated with a pre-existing <c>(Type, TmdbId)</c> Media entry.</summary>
    public int ItemsReused { get; set; }

    /// <summary>Kodi items already present in the baseline run for which nothing changed.</summary>
    public int ItemsUnchanged { get; set; }

    /// <summary>Total number of new file links created (sum of per-item linked-file counts).</summary>
    public int FilesLinked { get; set; }

    /// <summary>Items whose Kodi path is covered by no mapping.</summary>
    public int UnmatchedPaths { get; set; }

    /// <summary>Items whose translated path matches no scanner-known file.</summary>
    public int NoScannedFiles { get; set; }

    /// <summary>Items referencing non-filesystem locations (unsupported URI schemes).</summary>
    public int UnsupportedLocations { get; set; }

    /// <summary>Identity or link conflicts detected (existing data always preserved).</summary>
    public int Conflicts { get; set; }

    /// <summary>Baseline items absent from this upload (left untouched).</summary>
    public int NoLongerInKodi { get; set; }

    /// <summary>Items routed to the review queue.</summary>
    public int NeedsReview { get; set; }

    /// <summary>Items skipped because the identity provider was unreachable.</summary>
    public int IdentityLookupFailures { get; set; }

    /// <summary>Music-video rows ignored.</summary>
    public int SkippedMusicVideos { get; set; }

    // Navigation

    /// <summary>Per-item outcome rows written by the pipeline (one per Kodi library item).</summary>
    public ICollection<ImportItemOutcome> Outcomes { get; set; } = [];
}
