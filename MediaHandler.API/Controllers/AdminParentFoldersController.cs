using MediaHandler.API.Models;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Dashboard.Commands.AssignParentFolder;
using MediaHandler.Application.Features.Dashboard.DTOs;
using MediaHandler.Application.Features.Dashboard.Queries.ListParentFolders;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MediaHandler.API.Controllers;

/// <summary>
///     Admin endpoints for browsing and assigning TMDB entries to NAS parent folders.
///     All endpoints require the <c>AdminOnly</c> policy.
/// </summary>
[ApiController]
[Route("api/v1/admin/parent-folders")]
[Authorize(Policy = "AdminOnly")]
[EnableRateLimiting("fixed")]
public class AdminParentFoldersController(ISender sender) : ControllerBase
{
    /// <summary>
    ///     List unique NAS parent folders aggregated from media file paths.
    ///     Supports optional filtering by <c>status</c> (NotAssigned, Assigned, InCollection).
    ///     Paginated with <c>page</c> and <c>pageSize</c> (max 100).
    /// </summary>
    [HttpGet("")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<ParentFolderGroupDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListParentFolders(
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var query = new ListParentFoldersQuery(status, page, pageSize);
        var pagedResult = await sender.Send(query, ct);

        var meta = new ApiResponseMeta(
            pagedResult.Page,
            pagedResult.PageSize,
            pagedResult.TotalCount,
            pagedResult.TotalPages);

        return Ok(ApiResponse<IReadOnlyList<ParentFolderGroupDto>>.Success(pagedResult.Items, meta));
    }

    /// <summary>
    ///     Assign a TMDB entry to all media files inside the specified parent folder.
    ///     Updates all linked <c>ScanItemDecision</c> records and <c>MediaFile.MediaId</c>.
    /// </summary>
    /// <param name="folderId">Deterministic folder GUID (SHA-256 of lower-invariant folder path).</param>
    /// <param name="request">TMDB assignment details.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPut("{folderId:guid}/assign")]
    [ProducesResponseType<ApiResponse<ParentFolderGroupDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AssignParentFolder(
        Guid folderId,
        [FromBody] AssignParentFolderRequest request,
        CancellationToken ct = default)
    {
        var command = new AssignParentFolderCommand(folderId, request.FolderPath, request.TmdbId, request.Kind);
        var result = await sender.Send(command, ct);

        if (!result.IsSuccess)
        {
            var error = result.Errors.FirstOrDefault() ?? "Unknown error";

            if (error.Contains("FOLDER_NOT_FOUND", StringComparison.OrdinalIgnoreCase))
                return NotFound(ApiResponse.Fail(new ApiError("FOLDER_NOT_FOUND",
                    $"No media files found under '{request.FolderPath}'.")));

            if (error.Contains("TMDB_ID_NOT_FOUND", StringComparison.OrdinalIgnoreCase))
                return UnprocessableEntity(ApiResponse.Fail(new ApiError("TMDB_ID_NOT_FOUND",
                    $"The TMDB id {request.TmdbId} does not correspond to a known movie or TV show.")));

            return BadRequest(ApiResponse.Fail(new ApiError("VALIDATION_ERROR", error)));
        }

        return Ok(ApiResponse<ParentFolderGroupDto>.Success(result.Value));
    }
}

/// <summary>Request body for <c>PUT /api/v1/admin/parent-folders/{folderId}/assign</c>.</summary>
public record AssignParentFolderRequest(
    /// <summary>Absolute path of the parent folder on the NAS.</summary>
    string FolderPath,
    /// <summary>TMDB id of the entry to assign.</summary>
    int TmdbId,
    /// <summary>Media type — Film or TvShow.</summary>
    MediaType Kind);

