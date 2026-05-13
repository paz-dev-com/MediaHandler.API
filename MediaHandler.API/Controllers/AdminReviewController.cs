// AdminReviewController — admin endpoints for managing the TMDB review queue.
// All endpoints: AdminOnly policy, fixed rate limiter, ApiResponse<T> envelope.

using MediaHandler.API.Contracts.Admin;
using MediaHandler.API.Models;
using MediaHandler.Application.Common.DTOs;
using MediaHandler.Application.Features.Review.Commands.BulkResolveReviewItems;
using MediaHandler.Application.Features.Review.Commands.ResolveReviewItem;
using MediaHandler.Application.Features.Review.Queries.ListReviewItems;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MediaHandler.API.Controllers;

/// <summary>
///     Admin endpoints for managing items in the TMDB review queue.
///     All endpoints require the <c>AdminOnly</c> policy.
/// </summary>
[ApiController]
[Route("api/v1/admin/review-items")]
[Authorize(Policy = "AdminOnly")]
[EnableRateLimiting("fixed")]
public class AdminReviewController(ISender sender) : ControllerBase
{
    /// <summary>
    ///     List items in the review queue (default: Open items only).
    ///     Supports filtering by <c>status</c>, <c>reason</c>, and <c>scanRunId</c>.
    ///     Paginated with <c>page</c> and <c>pageSize</c> (max 100).
    /// </summary>
    [HttpGet("")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<ReviewItemDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListReviewItems(
        [FromQuery] ReviewStatus? status = ReviewStatus.Open,
        [FromQuery] ReviewReason? reason = null,
        [FromQuery] Guid? scanRunId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var result = await sender.Send(
            new ListReviewItemsQuery(status, reason, scanRunId, page, pageSize), ct);

        if (!result.IsSuccess)
            return BadRequest(ApiResponse.Fail(new ApiError("VALIDATION_ERROR",
                result.Errors.FirstOrDefault() ?? "Invalid query parameters.")));

        var pagedResult = result.Value;
        var meta = new ApiResponseMeta(
            pagedResult.Page,
            pagedResult.PageSize,
            pagedResult.TotalCount,
            pagedResult.TotalPages);

        return Ok(ApiResponse<IReadOnlyList<ReviewItemDto>>.Success(pagedResult.Items, meta));
    }

    /// <summary>
    ///     Resolve a review item by assigning a TMDB id, dismissing it, deleting its underlying file,
    ///     or reopening it to allow re-processing.
    /// </summary>
    /// <param name="id">The review item id.</param>
    /// <param name="request">Resolution action and optional TMDB mapping.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:guid}/resolve")]
    [ProducesResponseType<ApiResponse<ReviewItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ResolveReviewItem(
        Guid id,
        [FromBody] ResolveReviewRequest request,
        CancellationToken ct = default)
    {
        var command = new ResolveReviewItemCommand(id, request.Action, request.TmdbId, request.Kind);

        var result = await sender.Send(command, ct);

        if (!result.IsSuccess)
        {
            var error = result.Errors.FirstOrDefault() ?? "Unknown error";

            if (error.Contains("not found", StringComparison.OrdinalIgnoreCase)
                || error.Contains("was not found", StringComparison.OrdinalIgnoreCase))
                return NotFound(ApiResponse.Fail(new ApiError("NOT_FOUND",
                    $"ReviewItem '{id}' was not found.")));

            if (error.Contains("REVIEW_ALREADY_RESOLVED", StringComparison.OrdinalIgnoreCase))
                return Conflict(ApiResponse.Fail(new ApiError("REVIEW_ALREADY_RESOLVED",
                    "This review item has already been resolved or dismissed.")));

            if (error.Contains("REVIEW_ALREADY_OPEN", StringComparison.OrdinalIgnoreCase))
                return Conflict(ApiResponse.Fail(new ApiError("REVIEW_ALREADY_OPEN",
                    "This review item is already Open and cannot be reopened.")));

            if (error.Contains("TMDB_ID_NOT_FOUND", StringComparison.OrdinalIgnoreCase))
                return UnprocessableEntity(ApiResponse.Fail(new ApiError("TMDB_ID_NOT_FOUND",
                    $"The TMDB id {request.TmdbId} does not correspond to a known movie or TV show.")));

            return BadRequest(ApiResponse.Fail(new ApiError("VALIDATION_ERROR", error)));
        }

        return Ok(ApiResponse<ReviewItemDto>.Success(result.Value));
    }

    /// <summary>
    ///     Resolve all Open review items whose file path is inside <paramref name="request" />'s
    ///     <c>ParentFolderPath</c>, applying the same action to every matched item in one call.
    /// </summary>
    [HttpPost("bulk-resolve")]
    [ProducesResponseType<ApiResponse<BulkResolveResult>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> BulkResolveReviewItems(
        [FromBody] BulkResolveReviewRequest request,
        CancellationToken ct = default)
    {
        var command = new BulkResolveReviewItemsCommand(
            request.ParentFolderPath,
            request.Action,
            request.TmdbId,
            request.Kind);

        var result = await sender.Send(command, ct);

        if (!result.IsSuccess)
        {
            var error = result.Errors.FirstOrDefault() ?? "Unknown error";

            if (error.Contains("TMDB_ID_NOT_FOUND", StringComparison.OrdinalIgnoreCase))
                return UnprocessableEntity(ApiResponse.Fail(new ApiError("TMDB_ID_NOT_FOUND",
                    $"The TMDB id {request.TmdbId} does not correspond to a known movie or TV show.")));

            return BadRequest(ApiResponse.Fail(new ApiError("VALIDATION_ERROR", error)));
        }

        return Ok(ApiResponse<BulkResolveResult>.Success(result.Value));
    }
}