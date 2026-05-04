using MediaHandler.API.Contracts.Admin;
using MediaHandler.API.Models;
using MediaHandler.Application.Features.Dashboard.Commands.AssignTvGroup;
using MediaHandler.Application.Features.Dashboard.Commands.BatchRenameTvGroup;
using MediaHandler.Application.Features.Dashboard.Commands.ReassignTmdb;
using MediaHandler.Application.Features.Dashboard.DTOs;
using MediaHandler.Application.Features.Dashboard.Queries.ListTvShowGroups;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MediaHandler.API.Controllers;

/// <summary>
///     Admin endpoints for managing individual scan-item decisions.
///     All endpoints require the <c>AdminOnly</c> policy.
/// </summary>
[ApiController]
[Route("api/v1/admin/scan-decisions")]
[Authorize(Policy = "AdminOnly")]
[EnableRateLimiting("fixed")]
public class AdminScanDecisionsController(ISender sender) : ControllerBase
{
    /// <summary>
    ///     Reassign the TMDB match for a scan-item decision and update the linked media file.
    ///     Returns the updated decision with the new TMDB assignment details.
    /// </summary>
    [HttpPut("{id:guid}/reassign")]
    [ProducesResponseType<ApiResponse<ReassignTmdbResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ReassignTmdb(
        Guid id,
        [FromBody] ReassignTmdbRequest request,
        CancellationToken ct)
    {
        var result = await sender.Send(
            new ReassignTmdbCommand(id, request.TmdbId, request.MediaType), ct);

        if (!result.IsSuccess)
        {
            var error = result.Errors.FirstOrDefault() ?? "Unknown error";

            if (error.StartsWith("DECISION_NOT_FOUND", StringComparison.OrdinalIgnoreCase))
                return NotFound(ApiResponse.Fail(new ApiError("DECISION_NOT_FOUND",
                    $"ScanItemDecision '{id}' was not found.")));

            if (error.StartsWith("TMDB_ID_NOT_FOUND", StringComparison.OrdinalIgnoreCase))
                return UnprocessableEntity(ApiResponse.Fail(new ApiError("TMDB_ID_NOT_FOUND",
                    $"The TMDB id {request.TmdbId} does not correspond to a known movie or TV show.")));

            return BadRequest(ApiResponse.Fail(new ApiError("VALIDATION_ERROR", error)));
        }

        var r = result.Value;
        var response = new ReassignTmdbResponse(
            r.Id,
            r.AssignedTmdbId,
            r.AssignedTmdbKind,
            r.AssignedTitle,
            r.AssignedYear,
            r.MediaFileId,
            r.MediaId);

        return Ok(ApiResponse<ReassignTmdbResponse>.Success(response));
    }

    /// <summary>
    ///     Returns on-the-fly TV show groups computed from <c>ScanItemDecision</c> rows
    ///     for the specified scan run.  Groups are keyed by parsed show name and each carries
    ///     a deterministic <c>GroupId</c> suitable for the group assignment endpoint.
    /// </summary>
    [HttpGet("tv-groups")]
    [ProducesResponseType<ApiResponse<List<TvShowGroupDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListTvShowGroups(
        [FromQuery] Guid scanId,
        CancellationToken ct)
    {
        var result = await sender.Send(new ListTvShowGroupsQuery(scanId), ct);

        if (!result.IsSuccess)
        {
            var error = result.Errors.FirstOrDefault() ?? "Unknown error";
            return BadRequest(ApiResponse.Fail(new ApiError("VALIDATION_ERROR", error)));
        }

        return Ok(ApiResponse<List<TvShowGroupDto>>.Success(result.Value));
    }

    /// <summary>
    ///     Assigns a TMDB TV show to all episode decisions belonging to the specified group,
    ///     propagating the assignment to every linked <c>MediaFile</c>.
    ///     Uses a route override because the TV-groups resource lives at a different prefix
    ///     (<c>/api/v1/admin/tv-groups</c>) than this controller's base route.
    /// </summary>
    [HttpPut("~/api/v1/admin/tv-groups/{groupId:guid}/assign")]
    [ProducesResponseType<ApiResponse<AssignTvGroupResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AssignTvGroup(
        Guid groupId,
        [FromQuery] Guid scanId,
        [FromBody] AssignTvGroupRequest request,
        CancellationToken ct)
    {
        var result = await sender.Send(
            new AssignTvGroupCommand(groupId, scanId, request.TmdbId), ct);

        if (!result.IsSuccess)
        {
            var error = result.Errors.FirstOrDefault() ?? "Unknown error";

            if (error.StartsWith("GROUP_NOT_FOUND", StringComparison.OrdinalIgnoreCase))
                return NotFound(ApiResponse.Fail(new ApiError("GROUP_NOT_FOUND",
                    $"TV show group '{groupId}' was not found in scan '{scanId}'.")));

            if (error.StartsWith("TMDB_ID_NOT_FOUND", StringComparison.OrdinalIgnoreCase))
                return UnprocessableEntity(ApiResponse.Fail(new ApiError("TMDB_ID_NOT_FOUND",
                    $"The TMDB id {request.TmdbId} does not correspond to a known TV show.")));

            return BadRequest(ApiResponse.Fail(new ApiError("VALIDATION_ERROR", error)));
        }

        var r = result.Value;
        var response = new AssignTvGroupResponse(
            r.GroupId,
            r.ParsedShowName,
            r.EpisodeCount,
            r.AssignedTmdbId,
            r.AssignedTmdbKind,
            r.AssignedTitle,
            r.AssignedYear,
            r.AssignedPosterPath);

        return Ok(ApiResponse<AssignTvGroupResponse>.Success(response));
    }

    /// <summary>
    ///     Preview or execute a batch TMDB-convention rename for all episode files in a TV show group.
    ///     When <paramref name="preview" /> is <c>true</c> (default), returns proposed filenames
    ///     without touching the filesystem or database.
    ///     Validates ALL targets before executing ANY — rejects entire batch on any conflict.
    ///     Uses a route override because the TV-groups resource lives at a different prefix
    ///     (<c>/api/v1/admin/tv-groups</c>) than this controller's base route.
    /// </summary>
    [HttpPost("~/api/v1/admin/tv-groups/{groupId:guid}/rename")]
    [ProducesResponseType<ApiResponse<BatchRenameResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> BatchRenameTvGroup(
        Guid groupId,
        [FromQuery] Guid scanId,
        [FromQuery] bool preview = true,
        CancellationToken ct = default)
    {
        var result = await sender.Send(
            new BatchRenameTvGroupCommand(groupId, scanId, preview), ct);

        if (!result.IsSuccess)
        {
            var error = result.Errors.FirstOrDefault() ?? "Unknown error";

            if (error.StartsWith("GROUP_NOT_FOUND", StringComparison.OrdinalIgnoreCase))
                return NotFound(ApiResponse.Fail(new ApiError("GROUP_NOT_FOUND",
                    $"TV show group '{groupId}' was not found in scan '{scanId}'.")));

            if (error.StartsWith("TMDB_ASSIGNMENT_REQUIRED", StringComparison.OrdinalIgnoreCase))
                return UnprocessableEntity(ApiResponse.Fail(new ApiError("TMDB_ASSIGNMENT_REQUIRED",
                    "One or more episodes in this group have no TMDB assignment. " +
                    "Run group assignment first.")));

            if (error.StartsWith("EPISODE_TITLE_NOT_AVAILABLE", StringComparison.OrdinalIgnoreCase))
                return UnprocessableEntity(ApiResponse.Fail(new ApiError("EPISODE_TITLE_NOT_AVAILABLE",
                    "Episode title not available for one or more episodes — " +
                    "run TMDB enrichment first.")));

            if (error.StartsWith("FILE_CONFLICT", StringComparison.OrdinalIgnoreCase))
                return UnprocessableEntity(ApiResponse.Fail(new ApiError("FILE_CONFLICT",
                    ExtractMessage(error))));

            if (error.StartsWith("FILE_NOT_FOUND", StringComparison.OrdinalIgnoreCase))
                return NotFound(ApiResponse.Fail(new ApiError("FILE_NOT_FOUND",
                    ExtractMessage(error))));

            return BadRequest(ApiResponse.Fail(new ApiError("VALIDATION_ERROR", error)));
        }

        var r = result.Value;
        var executedCount = r.Episodes.Count(e => e.Executed);
        var response = new BatchRenameResponse(
            r.GroupId,
            r.ParsedShowName,
            r.Episodes,
            r.Episodes.Count,
            executedCount);

        return Ok(ApiResponse<BatchRenameResponse>.Success(response));
    }

    private static string ExtractMessage(string error)
    {
        var colonIdx = error.IndexOf(':');
        return colonIdx >= 0 && colonIdx < error.Length - 2
            ? error[(colonIdx + 2)..]
            : error;
    }
}

