using MediaHandler.Domain.Common;
using MediaHandler.Domain.Enums;

namespace MediaHandler.Domain.Entities;

/// <summary>
///     Audit record capturing the import pipeline's outcome for a single Kodi library item
///     within an <see cref="ImportRun" />.
///     Every Kodi row the pipeline touches — including skipped music videos — produces exactly
///     one outcome row, plus synthesized <see cref="ImportItemStatus.NoLongerInKodi" /> rows
///     for baseline items absent from the upload.
/// </summary>
public class ImportItemOutcome : BaseEntity
{
    /// <summary>The run that produced this outcome.</summary>
    public required Guid ImportRunId { get; set; }

    /// <summary>Which Kodi table the item originates from.</summary>
    public required KodiItemKind KodiItemKind { get; set; }

    /// <summary>The Kodi-internal id of the item (<c>idMovie</c> / <c>idShow</c> / <c>idEpisode</c> / <c>idMVideo</c>).</summary>
    public required int KodiItemId { get; set; }

    /// <summary>
    ///     Kodi title of the item. For <see cref="ImportItemStatus.NoLongerInKodi" /> rows this is
    ///     the title recorded by the baseline run.
    /// </summary>
    public required string Title { get; set; }

    /// <summary>Resolved media kind; <c>null</c> for music videos.</summary>
    public MediaType? MediaKind { get; set; }

    /// <summary>Item-level outcome assigned by the pipeline.</summary>
    public required ImportItemStatus Outcome { get; set; }

    /// <summary>File-link outcome; <c>null</c> when no link was attempted or applicable.</summary>
    public ImportLinkStatus? LinkOutcome { get; set; }

    /// <summary>Number of files newly linked for this item.</summary>
    public int LinkedFileCount { get; set; }

    /// <summary>Human-readable explanation; populated for non-success outcomes.</summary>
    public string? Reason { get; set; }

    /// <summary>
    ///     Normalized Kodi <em>directory</em> URI involved in a link failure —
    ///     the actionable prefix for extending path mappings.
    /// </summary>
    public string? KodiPathPrefix { get; set; }

    /// <summary>The <c>Media</c> entry the item resolved to, when applicable.</summary>
    public Guid? MediaId { get; set; }

    /// <summary>The primary <c>MediaFile</c> linked for this item, when applicable.</summary>
    public Guid? MediaFileId { get; set; }

    // Navigation

    public ImportRun ImportRun { get; set; } = null!;
}
