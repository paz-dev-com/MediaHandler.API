using MediaHandler.Domain.Common;
using MediaHandler.Domain.Enums;

namespace MediaHandler.Domain.Entities;

/// <summary>
///     A configured NAS path that the scanner monitors.
///     Each root is typed so the pipeline can apply the correct Kodi-equivalent
///     classification heuristics (movie vs. TV-show folder layout).
/// </summary>
public class LibraryRoot : BaseEntity
{
    /// <summary>
    ///     Canonical absolute path on the NAS (e.g., <c>/nas/Movies</c>).
    ///     Must start with one of the paths returned by <c>INasService.GetConfiguredPathsAsync</c>.
    ///     Unique — enforced by a unique index in <c>LibraryRootConfiguration</c>.
    /// </summary>
    public required string Path { get; set; }

    /// <summary>Content type hosted under this root.</summary>
    public required LibraryRootKind Kind { get; set; }

    /// <summary>Optional human-readable label shown in the admin UI.</summary>
    public string? Label { get; set; }

    /// <summary>
    ///     When <c>false</c> the root is skipped during scans without being deleted.
    ///     Defaults to <c>true</c>.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    // ── Navigation ──────────────────────────────────────────────────────────

    /// <summary>All <c>MediaFile</c> rows whose physical location lies under this root.</summary>
    public ICollection<MediaFile> MediaFiles { get; set; } = [];
}