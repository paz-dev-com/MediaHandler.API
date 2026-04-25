using MediaHandler.Domain.Common;
using MediaHandler.Domain.Enums;

namespace MediaHandler.Domain.Entities;

public class MediaFile : BaseEntity
{
    public Guid? MediaId { get; set; }
    public required string FilePath { get; set; }
    public long? FileSizeBytes { get; set; }
    public string? Format { get; set; }
    public string? Resolution { get; set; }

    // ── Scanner additions (T022) ─────────────────────────────────────────────

    /// <summary>
    /// SHA-256 hex digest of <c>absPath|size|mtimeUnix</c> used for fast incremental
    /// change detection (R-006). Indexed uniquely per <c>(LibraryRootId, Fingerprint)</c>.
    /// </summary>
    public required string Fingerprint { get; set; }

    /// <summary>Last-modified timestamp of the physical file on the NAS, in UTC.</summary>
    public DateTime? MtimeUtc { get; set; }

    /// <summary>
    /// FK to the <see cref="StackGroup"/> this file belongs to.
    /// <c>null</c> for non-stacked files.
    /// </summary>
    public Guid? StackGroupId { get; set; }

    /// <summary>Stack descriptor; populated when <see cref="StackGroupId"/> is set.</summary>
    public StackGroup? StackGroup { get; set; }

    /// <summary>The <see cref="LibraryRoot"/> under which this file lives.</summary>
    public required Guid LibraryRootId { get; set; }

    /// <summary>Navigation to the owning <see cref="LibraryRoot"/>.</summary>
    public LibraryRoot LibraryRoot { get; set; } = null!;

    /// <summary>Functional role of this file within its logical media item.</summary>
    public required MediaFileRole Role { get; set; }

    /// <summary>The <see cref="ScanRun"/> in which this file was first discovered.</summary>
    public Guid FirstSeenScanRunId { get; set; }

    /// <summary>The most recent <see cref="ScanRun"/> in which this file was still present.</summary>
    public Guid? LastSeenScanRunId { get; set; }

    /// <summary>
    /// UTC timestamp set when this file was absent from the NAS during a scan.
    /// <c>null</c> means the file is still present.  Indexed for the missing-file
    /// cleanup query (R-007).
    /// </summary>
    public DateTime? MissingSince { get; set; }

    // ── Navigation ──────────────────────────────────────────────────────────

    public Media? Media { get; set; }

    /// <summary>
    /// Episode links for TV-episode files; may contain more than one entry for
    /// multi-episode files (e.g., <c>S02E05-E06</c>).
    /// </summary>
    public ICollection<EpisodeFileLink> EpisodeLinks { get; set; } = [];
}
