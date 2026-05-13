using MediaHandler.API.Contracts.Admin;
using MediaHandler.API.Models;
using MediaHandler.Application.Features.Dashboard.Commands.StartEnrichment;
using MediaHandler.Application.Features.Dashboard.DTOs;
using MediaHandler.Application.Features.Dashboard.Queries.GetEnrichmentRunDetails;
using MediaHandler.Application.Features.Dashboard.Queries.GetEnrichmentStatus;
using MediaHandler.Application.Features.Dashboard.Queries.GetEnrichmentSummary;
using MediaHandler.Application.Features.Dashboard.Queries.ListEnrichmentHistory;
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

    /// <summary>
    ///     Returns a pre-flight summary of how many media entries would be processed by a new enrichment run:
    ///     new (never enriched), changed (updated since last run), skipped (already up-to-date), and total eligible.
    /// </summary>
    [HttpGet("summary")]
    [ProducesResponseType<ApiResponse<EnrichmentSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetEnrichmentSummary(CancellationToken ct)
    {
        var result = await sender.Send(new GetEnrichmentSummaryQuery(), ct);

        if (!result.IsSuccess)
        {
            var error = result.Errors.FirstOrDefault() ?? "Unknown error";
            return BadRequest(ApiResponse.Fail(new ApiError("QUERY_ERROR", error)));
        }

        return Ok(ApiResponse<EnrichmentSummaryDto>.Success(result.Value));
    }

    /// <summary>
    ///     Returns a paginated list of past enrichment runs, ordered by most recent first.
    /// </summary>
    [HttpGet("history")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<EnrichmentRunDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListEnrichmentHistory(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var pagedResult = await sender.Send(new ListEnrichmentHistoryQuery(page, pageSize), ct);

        var meta = new ApiResponseMeta(
            pagedResult.Page,
            pagedResult.PageSize,
            pagedResult.TotalCount,
            pagedResult.TotalPages);

        return Ok(ApiResponse<IReadOnlyList<EnrichmentRunDto>>.Success(pagedResult.Items, meta));
    }

    /// <summary>
    ///     Returns a detailed per-media breakdown for a specific enrichment run.
    ///     Lists every media entry processed, its outcome (Enriched / Failed / Skipped),
    ///     associated file names, and any error message for failed entries.
    /// </summary>
    [HttpGet("{runId:guid}/details")]
    [ProducesResponseType<ApiResponse<List<EnrichmentMediaDetailDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetEnrichmentRunDetails(
        Guid runId,
        CancellationToken ct)
    {
        var result = await sender.Send(new GetEnrichmentRunDetailsQuery(runId), ct);

        if (!result.IsSuccess)
        {
            var error = result.Errors.FirstOrDefault() ?? "Unknown error";

            if (error.StartsWith("ENRICHMENT_RUN_NOT_FOUND", StringComparison.OrdinalIgnoreCase))
                return NotFound(ApiResponse.Fail(
                    new ApiError("ENRICHMENT_RUN_NOT_FOUND", $"Enrichment run '{runId}' was not found.")));

            return BadRequest(ApiResponse.Fail(new ApiError("QUERY_ERROR", error)));
        }

        return Ok(ApiResponse<List<EnrichmentMediaDetailDto>>.Success(result.Value));
    }
}

