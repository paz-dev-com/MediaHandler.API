using MediaHandler.API.Contracts.Admin;
using MediaHandler.API.Models;
using MediaHandler.Application.Features.Dashboard.Commands.ReassignTmdb;
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
}

