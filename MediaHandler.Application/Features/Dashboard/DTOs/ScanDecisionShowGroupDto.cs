namespace MediaHandler.Application.Features.Dashboard.DTOs;

/// <summary>
///     A show-level group of scan decisions, with the episodes collapsed under a single header.
///     Used by the grouped scan decisions endpoint.
/// </summary>
public record ScanDecisionShowGroupDto(
    /// <summary>
    ///     Deterministic group ID computed as SHA-256(scanId|parsedTitle.ToLower()) with UUID-v5 bits.
    ///     Null for single-item (movie) pseudo-groups.
    /// </summary>
    Guid? GroupId,
    /// <summary>Normalized show name used as the group key.</summary>
    string ShowName,
    /// <summary>Number of episode decisions in this group.</summary>
    int EpisodeCount,
    /// <summary>TMDB id assigned to the majority of episodes (null if not assigned).</summary>
    int? AssignedTmdbId,
    /// <summary>TMDB kind (Film/TvShow) assigned to the majority of episodes.</summary>
    string? AssignedKind,
    /// <summary>Title from the TMDB assignment (majority).</summary>
    string? AssignedTitle,
    /// <summary>Year from the TMDB assignment (majority).</summary>
    int? AssignedYear,
    /// <summary>Poster path from the TMDB assignment (majority).</summary>
    string? AssignedPosterPath,
    /// <summary>All individual episode decisions collapsed under this group.</summary>
    IReadOnlyList<ScanItemDecisionDto> Episodes);

