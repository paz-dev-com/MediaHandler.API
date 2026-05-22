using MediaHandler.API.Contracts.Admin;
using MediaHandler.API.Models;
using MediaHandler.Application.Common.DTOs;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Dashboard.DTOs;
using MediaHandler.Application.Features.Dashboard.Queries.ListScanDecisions;
using MediaHandler.Application.Features.Dashboard.Queries.ListGroupedScanDecisions;
using MediaHandler.Application.Features.Scan.Commands.CancelScan;
using MediaHandler.Application.Features.Scan.Commands.StartScan;
using MediaHandler.Application.Features.Scan.Queries.GetActiveScan;
using MediaHandler.Application.Features.Scan.Queries.GetScanRun;
using MediaHandler.Application.Features.Scan.Queries.ListScanHistory;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MediaHandler.API.Controllers;

/// <summary>
///     Admin endpoints for managing NAS scan runs.
///     All endpoints require the <c>AdminOnly</c> policy.
/// </summary>
[ApiController]
[Route("api/v1/admin/scan")]
[Authorize(Policy = "AdminOnly")]
[EnableRateLimiting("fixed")]
public class AdminScanController(ISender sender) : ControllerBase
{
    /// <summary>
    ///     Start a new scan run. Returns 202 Accepted with the run summary.
    ///     The scan executes in the background; poll <c>GET /scan/{id}</c> for progress.
    ///     Returns 409 Conflict when another scan is already running.
    /// </summary>
    [HttpPost("")]
    [ProducesResponseType<ApiResponse<ScanRunSummaryResponse>>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> StartScan([FromBody] StartScanRequest request, CancellationToken ct)
    {
        var result = await sender.Send(new StartScanCommand(request.LibraryRootIds, request.Mode, request.Language), ct);

        if (!result.IsSuccess)
        {
            var error = result.Errors.FirstOrDefault() ?? "Unknown error";

            if (error.Contains("SCAN_IN_PROGRESS", StringComparison.OrdinalIgnoreCase))
                return Conflict(ApiResponse.Fail(new ApiError("SCAN_IN_PROGRESS",
                    "A scan is already running. Wait for it to complete or cancel it.")));

            return BadRequest(ApiResponse.Fail(new ApiError("VALIDATION_ERROR", error)));
        }

        // Re-read the newly created scan run to build the response
        var detailResult = await sender.Send(new GetScanRunQuery(result.Value.ScanRunId, false), ct);
        if (!detailResult.IsSuccess)
            return StatusCode(StatusCodes.Status202Accepted,
                ApiResponse<ScanRunSummaryResponse>.Success(new ScanRunSummaryResponse(
                    result.Value.ScanRunId,
                    request.Mode,
                    ScanStatus.Pending,
                    DateTime.UtcNow,
                    null,
                    request.LibraryRootIds,
                    new ScanCountsDto(0, 0, 0, 0, 0, 0, 0))));

        var d = detailResult.Value;
        var summary = new ScanRunSummaryResponse(d.Id, d.Mode, d.Status, d.StartedAt, d.FinishedAt, d.LibraryRootIds,
            d.Counts);

        return StatusCode(
            StatusCodes.Status202Accepted,
            ApiResponse<ScanRunSummaryResponse>.Success(summary));
    }

    /// <summary>
    ///     Fetch a single scan run by id.
    ///     When <c>includeReview=true</c>, attaches up to 100 of the most recent open review items.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ApiResponse<ScanRunDetailResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetScanRun(
        Guid id,
        [FromQuery] bool includeReview = false,
        CancellationToken ct = default)
    {
        var result = await sender.Send(new GetScanRunQuery(id, includeReview), ct);

        if (!result.IsSuccess)
            return NotFound(ApiResponse.Fail(new ApiError("NOT_FOUND",
                $"Scan run '{id}' was not found.")));

        var d = result.Value;
        var response = new ScanRunDetailResponse(
            d.Id, d.Mode, d.Status, d.StartedAt, d.FinishedAt, d.FailureReason,
            d.LibraryRootIds, d.Counts, d.ReviewItems);

        return Ok(ApiResponse<ScanRunDetailResponse>.Success(response));
    }

    /// <summary>
    ///     Returns the currently running scan, or a 200 response with <c>null</c> data when no scan is active.
    /// </summary>
    [HttpGet("active")]
    [ProducesResponseType<ApiResponse<ScanRunSummaryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetActiveScan(CancellationToken ct)
    {
        var result = await sender.Send(new GetActiveScanQuery(), ct);

        if (result.Value is null)
            return Ok(ApiResponse<ScanRunSummaryResponse>.Success(null!));

        var d = result.Value;
        var summary = new ScanRunSummaryResponse(d.Id, d.Mode, d.Status, d.StartedAt, d.FinishedAt, d.LibraryRootIds,
            d.Counts);
        return Ok(ApiResponse<ScanRunSummaryResponse>.Success(summary));
    }

    /// <summary>
    ///     Request cancellation of a running scan. Idempotent — cancelling an already-finished
    ///     scan returns 200 with the current terminal state.
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType<ApiResponse<ScanRunSummaryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelScan(Guid id, CancellationToken ct)
    {
        var result = await sender.Send(new CancelScanCommand(id), ct);

        if (!result.IsSuccess)
            return NotFound(ApiResponse.Fail(new ApiError("NOT_FOUND",
                $"Scan run '{id}' was not found.")));

        var d = result.Value;
        var summary = new ScanRunSummaryResponse(
            d.Id, d.Mode, d.Status, d.StartedAt, d.FinishedAt, d.LibraryRootIds, d.Counts);

        return Ok(ApiResponse<ScanRunSummaryResponse>.Success(summary));
    }

    /// <summary>
    ///     Browse the scan-run history, ordered by start time descending.
    ///     Paginated with <c>page</c> and <c>pageSize</c> (max 100, default 20).
    /// </summary>
    [HttpGet("")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<ScanRunDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListHistory(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? sortField = null,
        [FromQuery] string? sortOrder = "asc",
        CancellationToken ct = default)
    {
        var result = await sender.Send(new ListScanHistoryQuery(page, pageSize, sortField, sortOrder), ct);

        if (!result.IsSuccess)
            return BadRequest(ApiResponse.Fail(new ApiError("VALIDATION_ERROR",
                result.Errors.FirstOrDefault() ?? "Invalid query parameters.")));

        var pagedResult = result.Value;
        var meta = new ApiResponseMeta(
            pagedResult.Page,
            pagedResult.PageSize,
            pagedResult.TotalCount,
            pagedResult.TotalPages);

        return Ok(ApiResponse<IReadOnlyList<ScanRunDto>>.Success(pagedResult.Items, meta));
    }

    /// <summary>
    ///     Browse all <c>ScanItemDecision</c> records for a scan run with optional filters.
    ///     Paginated with <c>page</c> and <c>pageSize</c> (max 100).
    /// </summary>
    [HttpGet("{scanId:guid}/decisions")]
    [ProducesResponseType<ApiResponse<PagedResult<ScanItemDecisionDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListDecisions(
        Guid scanId,
        [FromQuery] ScanDecisionKind? decisionType = null,
        [FromQuery] MediaType? mediaType = null,
        [FromQuery] Guid? libraryRootId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? sortField = null,
        [FromQuery] string? sortOrder = "asc",
        [FromQuery] string? fileName = null,
        CancellationToken ct = default)
    {
        var result = await sender.Send(
            new ListScanDecisionsQuery(scanId, decisionType, mediaType, libraryRootId, page, pageSize, sortField, sortOrder, fileName), ct);

        if (!result.IsSuccess)
            return BadRequest(ApiResponse.Fail(new ApiError("VALIDATION_ERROR",
                result.Errors.FirstOrDefault() ?? "Invalid query parameters.")));

        var pagedResult = result.Value;
        var meta = new ApiResponseMeta(
            pagedResult.Page,
            pagedResult.PageSize,
            pagedResult.TotalCount,
            pagedResult.TotalPages);

        // Return the items array directly in `data` (matching the frontend contract),
        // pagination info is carried by `meta`.
        return Ok(ApiResponse<IReadOnlyList<ScanItemDecisionDto>>.Success(pagedResult.Items, meta));
    }

    /// <summary>
    ///     Browse all <c>ScanItemDecision</c> records for a scan run, grouped by TV show.
    ///     TV show episodes are deduplicated by file path and collapsed under a show-level header.
    ///     Movie decisions remain as single-item groups.
    ///     Supports the same filters as the flat <c>decisions</c> endpoint.
    /// </summary>
    [HttpGet("{scanId:guid}/decisions/grouped")]
    [ProducesResponseType<ApiResponse<List<ScanDecisionShowGroupDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListGroupedDecisions(
        Guid scanId,
        [FromQuery] ScanDecisionKind? decisionType = null,
        [FromQuery] MediaType? mediaType = null,
        [FromQuery] Guid? libraryRootId = null,
        CancellationToken ct = default)
    {
        var result = await sender.Send(
            new ListGroupedScanDecisionsQuery(scanId, decisionType, mediaType, libraryRootId), ct);

        if (!result.IsSuccess)
            return BadRequest(ApiResponse.Fail(new ApiError("VALIDATION_ERROR",
                result.Errors.FirstOrDefault() ?? "Invalid query parameters.")));

        return Ok(ApiResponse<List<ScanDecisionShowGroupDto>>.Success(result.Value));
    }
}