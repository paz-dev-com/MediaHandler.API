using MediaHandler.Domain.Common;

namespace MediaHandler.Domain.Entities;

/// <summary>
///     Join-table for the many-to-many relationship between <see cref="TvEpisode" /> and
///     <see cref="MediaFile" />.
/// </summary>
/// <remarks>
///     <para>
///         A single physical file can carry multiple episodes (e.g., <c>S02E05-E06</c>),
///         and by symmetry a single episode could theoretically span multiple files
///         (rare but supported).
///     </para>
///     <para>
///         A composite unique constraint on <c>(TvEpisodeId, MediaFileId)</c> prevents
///         duplicate links.
///     </para>
/// </remarks>
public class EpisodeFileLink : BaseEntity
{
    /// <summary>The logical TV episode.</summary>
    public required Guid TvEpisodeId { get; set; }

    /// <summary>The physical media file.</summary>
    public required Guid MediaFileId { get; set; }

    /// <summary>
    ///     1-based position of this episode within the file.
    ///     For a single-episode file this is always <c>1</c>.
    ///     For a multi-episode file the first episode is <c>1</c>, the second is <c>2</c>, etc.
    /// </summary>
    public int OrderInFile { get; set; } = 1;

    // ── Navigation ──────────────────────────────────────────────────────────

    public TvEpisode TvEpisode { get; set; } = null!;
    public MediaFile MediaFile { get; set; } = null!;
}