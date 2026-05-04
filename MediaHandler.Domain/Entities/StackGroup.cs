using MediaHandler.Domain.Common;

namespace MediaHandler.Domain.Entities;

/// <summary>
///     Groups the physical parts of a multi-part (stacked) movie under a single
///     logical <see cref="Media" /> entry.
/// </summary>
/// <remarks>
///     Each part file is a <see cref="MediaFile" /> with
///     <see cref="MediaFileRole.StackedPart" /> and a FK to this group.
///     There is at most one <c>StackGroup</c> per <see cref="Media" /> row
///     (unique index on <c>MediaId</c>).
/// </remarks>
public class StackGroup : BaseEntity
{
    /// <summary>The logical movie this stack belongs to.</summary>
    public required Guid MediaId { get; set; }

    // Navigation

    public Media Media { get; set; } = null!;

    /// <summary>The ordered physical part files (cd1, cd2, disc1, disc2, …).</summary>
    public ICollection<MediaFile> Parts { get; set; } = [];
}