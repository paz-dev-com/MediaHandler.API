using MediaHandler.API.Contracts.Media;
using MediaHandler.API.Models;
using MediaHandler.Application.Features.Media.Commands.LinkMediaFile;
using MediaHandler.Application.Features.Media.Commands.UnlinkMediaFile;
using MediaHandler.Application.Features.Media.Commands.UpdateMediaRootFolder;
using MediaHandler.Application.Features.Media.DTOs;
using MediaHandler.Application.Features.Media.Queries.GetUnlinkedFiles;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MediaHandler.API.Controllers;

/// <summary>
///     Admin endpoints for linking/unlinking media files, updating root folder overrides,
///     and browsing unlinked files.
///     All endpoints require the <c>AdminOnly</c> policy.
/// </summary>
[ApiController]
[Route("api/v1/admin/media")]
[Authorize(Policy = "AdminOnly")]
[EnableRateLimiting("fixed")]
public class AdminMediaFilesController(ISender sender) : ControllerBase
{
    /// <summary>Links a media file to a media item (idempotent).</summary>
    [HttpPut("{mediaId:guid}/files/{fileId:guid}/link")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> LinkFile(Guid mediaId, Guid fileId, CancellationToken ct)
    {
        var result = await sender.Send(new LinkMediaFileCommand(mediaId, fileId), ct);
        if (!result.IsSuccess)
        {
            var error = result.Errors.FirstOrDefault() ?? string.Empty;
            if (error.StartsWith("NOT_FOUND", StringComparison.OrdinalIgnoreCase))
                return NotFound(ApiResponse.Fail(new ApiError("NOT_FOUND", error)));
            if (error.StartsWith("FILE_ALREADY_LINKED", StringComparison.OrdinalIgnoreCase))
                return UnprocessableEntity(ApiResponse.Fail(new ApiError("FILE_ALREADY_LINKED", error)));
            return BadRequest(ApiResponse.Fail(new ApiError("VALIDATION_ERROR", error)));
        }
        return Ok(ApiResponse<object>.Success(new { }));
    }

    /// <summary>Unlinks a media file from a media item.</summary>
    [HttpDelete("{mediaId:guid}/files/{fileId:guid}/link")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnlinkFile(Guid mediaId, Guid fileId, CancellationToken ct)
    {
        var result = await sender.Send(new UnlinkMediaFileCommand(mediaId, fileId), ct);
        if (!result.IsSuccess)
        {
            var error = result.Errors.FirstOrDefault() ?? string.Empty;
            if (error.StartsWith("NOT_FOUND", StringComparison.OrdinalIgnoreCase))
                return NotFound(ApiResponse.Fail(new ApiError("NOT_FOUND", error)));
            return BadRequest(ApiResponse.Fail(new ApiError("VALIDATION_ERROR", error)));
        }
        return Ok(ApiResponse<object>.Success(new { }));
    }

    /// <summary>Sets or clears the root folder override for a media item.</summary>
    [HttpPatch("{mediaId:guid}/root-folder")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateRootFolder(
        Guid mediaId,
        [FromBody] UpdateRootFolderRequest request,
        CancellationToken ct)
    {
        var result = await sender.Send(new UpdateMediaRootFolderCommand(mediaId, request.RootFolder), ct);
        if (!result.IsSuccess)
        {
            var error = result.Errors.FirstOrDefault() ?? string.Empty;
            if (error.StartsWith("NOT_FOUND", StringComparison.OrdinalIgnoreCase))
                return NotFound(ApiResponse.Fail(new ApiError("NOT_FOUND", error)));
            return BadRequest(ApiResponse.Fail(new ApiError("VALIDATION_ERROR", error)));
        }
        return Ok(ApiResponse<object>.Success(new { }));
    }

    /// <summary>Returns a paged list of media files not linked to any media item.</summary>
    [HttpGet("unlinked-files")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<UnlinkedFileDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetUnlinkedFiles(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await sender.Send(new GetUnlinkedFilesQuery(page, pageSize), ct);
        if (!result.IsSuccess)
        {
            var error = result.Errors.FirstOrDefault() ?? string.Empty;
            return BadRequest(ApiResponse.Fail(new ApiError("VALIDATION_ERROR", error)));
        }
        var meta = new ApiResponseMeta(result.Value.Page, result.Value.PageSize,
            result.Value.TotalCount, result.Value.TotalPages);
        return Ok(ApiResponse<IReadOnlyList<UnlinkedFileDto>>.Success(result.Value.Items, meta));
    }
}
