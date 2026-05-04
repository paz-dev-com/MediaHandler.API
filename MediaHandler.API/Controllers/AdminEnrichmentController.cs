using MediaHandler.API.Contracts.Admin;
using MediaHandler.API.Models;
using MediaHandler.Application.Features.Dashboard.Commands.StartEnrichment;
using MediaHandler.Application.Features.Dashboard.Queries.GetEnrichmentStatus;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MediaHandler.API.Controllers;

/// <summary>
///     Admin endpoints for managing batch TMDB enrichment runs.
///     All endpoints require the <c>AdminOnly</c> policy.
/// </summary>
[ApiController]
[Route("api/v1/admin/enrichment")]
[Authorize(Policy = "AdminOnly")]
[EnableRateLimiting("fixed")]
public class AdminEnrichmentController(ISender sender) : ControllerBase
{
    /// <summary>
    ///     Starts a new background TMDB batch enrichment run.
    ///     Returns <c>202 Accepted</c> when a run is started, or <c>200 OK</c> when there are
    ///     no eligible media entries to enrich.
    ///     Returns <c>409 Conflict</c> when an enrichment run is already in progress.
    /// </summary>
    [HttpPost("start")]
    [ProducesResponseType<ApiResponse<StartEnrichmentResponse>>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ApiResponse<StartEnrichmentResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> StartEnrichment(CancellationToken ct)
    {
        var result = await sender.Send(new StartEnrichmentCommand(), ct);

        if (!result.IsSuccess)
        {
            var error = result.Errors.FirstOrDefault() ?? "Unknown error";

            if (error.StartsWith("ENRICHMENT_ALREADY_RUNNING", StringComparison.OrdinalIgnoreCase))
                return Conflict(ApiResponse.Fail(
                    new ApiError("ENRICHMENT_ALREADY_RUNNING", "An enrichment run is already in progress.")));

            return BadRequest(ApiResponse.Fail(new ApiError("VALIDATION_ERROR", error)));
        }

        var r = result.Value;
        var response = new StartEnrichmentResponse(
            r.EnrichmentRunId,
            r.Status,
            r.TotalItems,
            r.WasStarted
                ? $"Enrichment run started with {r.TotalItems} item(s) queued."
                : "No eligible media entries found; nothing to enrich.");

        // 202 Accepted when a run was started; 200 OK when nothing to do.
        if (r.WasStarted)
            return StatusCode(StatusCodes.Status202Accepted,
                ApiResponse<StartEnrichmentResponse>.Success(response));

        return Ok(ApiResponse<StartEnrichmentResponse>.Success(response));
    }

    /// <summary>
    ///     Returns the status of the most recent enrichment run, or <c>null</c> data when
    ///     no enrichment run has ever been recorded.
    /// </summary>
    [HttpGet("status")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetEnrichmentStatus(CancellationToken ct)
    {
        var result = await sender.Send(new GetEnrichmentStatusQuery(), ct);

        if (!result.IsSuccess)
        {
            var error = result.Errors.FirstOrDefault() ?? "Unknown error";
            return BadRequest(ApiResponse.Fail(new ApiError("QUERY_ERROR", error)));
        }

        return Ok(ApiResponse<object>.Success(result.Value!));
    }
}

