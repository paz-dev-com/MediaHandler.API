using MediaHandler.Domain.Enums;

namespace MediaHandler.API.Contracts.Admin;

/// <summary>
///     Request body for <c>PUT /api/v1/admin/scan-decisions/{id}/reassign</c>.
/// </summary>
public record ReassignTmdbRequest(
    int TmdbId,
    MediaType Kind);

/// <summary>
///     Request body for <c>PUT /api/v1/admin/tv-groups/{groupId}/assign</c>.
/// </summary>
public record AssignTvGroupRequest(int TmdbId);

