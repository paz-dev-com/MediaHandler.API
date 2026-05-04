using MediaHandler.Application.Features.Dashboard.DTOs;
using MediaHandler.Domain.Enums;

namespace MediaHandler.API.Contracts.Admin;

/// <summary>
///     Response for <c>PUT /api/v1/admin/scan-decisions/{id}/reassign</c>.
/// </summary>
public record ReassignTmdbResponse(
    Guid Id,
    int? AssignedTmdbId,
    MediaType? AssignedTmdbKind,
    string? AssignedTitle,
    int? AssignedYear,
    Guid? MediaFileId,
    Guid? MediaId);

/// <summary>
///     Response for <c>PUT /api/v1/admin/tv-groups/{groupId}/assign</c>.
///     Contains the same shape as <see cref="TvShowGroupDto" /> with all TMDB assignment fields.
/// </summary>
public record AssignTvGroupResponse(
    Guid GroupId,
    string ParsedShowName,
    int EpisodeCount,
    int? AssignedTmdbId,
    MediaType? AssignedTmdbKind,
    string? AssignedTitle,
    int? AssignedYear,
    string? AssignedPosterPath);

/// <summary>
///     Response for <c>POST /api/v1/admin/enrichment/start</c>.
/// </summary>
public record StartEnrichmentResponse(
    Guid? EnrichmentRunId,
    EnrichmentStatus Status,
    int TotalItems,
    string Message);

/// <summary>
///     Response for <c>POST /api/v1/admin/tv-groups/{groupId}/rename</c>.
/// </summary>
public record BatchRenameResponse(
    Guid GroupId,
    string ParsedShowName,
    IReadOnlyList<FileRenameResultDto> Episodes,
    int TotalEpisodes,
    int ExecutedCount);

