namespace MediaHandler.Application.Features.Dashboard.DTOs;

/// <summary>
///     Represents a unique NAS parent folder aggregated from <c>MediaFile.FilePath</c>,
///     with its TMDB assignment status.
/// </summary>
public record ParentFolderGroupDto(
    /// <summary>Deterministic SHA-256 GUID derived from <see cref="FolderPath" /> (lower-invariant).</summary>
    Guid Id,
    /// <summary>Absolute path of the parent directory on the NAS.</summary>
    string FolderPath,
    /// <summary>Inferred show name from the last path segment.</summary>
    string DetectedShowName,
    /// <summary>Number of media files located directly inside this folder.</summary>
    int EpisodeCount,
    /// <summary>
    ///     Assignment status:
    ///     <list type="bullet">
    ///         <item><c>NotAssigned</c> — no TMDB assignment on any file in the folder.</item>
    ///         <item><c>Assigned</c> — TMDB assigned via <c>ScanItemDecision</c> but not yet enriched into the Media collection.</item>
    ///         <item><c>InCollection</c> — the linked <c>Media</c> row already has full TMDB metadata.</item>
    ///     </list>
    /// </summary>
    string Status,
    /// <summary>TMDB id of the assigned entry, when status is Assigned or InCollection.</summary>
    int? TmdbId,
    /// <summary>Title of the assigned TMDB entry.</summary>
    string? TmdbTitle);

