using MediaHandler.API.Models;
using MediaHandler.Application.Features.Dashboard.Commands.RenameFile;
using MediaHandler.Application.Features.Dashboard.DTOs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MediaHandler.API.Controllers;

/// <summary>
///     Admin endpoints for managing individual media files (rename, etc.).
///     All endpoints require the <c>AdminOnly</c> policy.
/// </summary>
[ApiController]
[Route("api/v1/admin/files")]
[Authorize(Policy = "AdminOnly")]
[EnableRateLimiting("fixed")]
public class AdminFilesController(ISender sender) : ControllerBase
{
    /// <summary>
    ///     Preview or execute a TMDB-convention rename for a single media file.
    ///     When <paramref name="preview" /> is <c>true</c> (default), returns the proposed
    ///     filename without touching the filesystem or database.
    ///     When <c>false</c>, performs an atomic rename and updates the database.
    /// </summary>
    [HttpPost("{id:guid}/rename")]
    [ProducesResponseType<ApiResponse<FileRenameResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RenameFile(
        Guid id,
        [FromQuery] bool preview = true,
        CancellationToken ct = default)
    {
        var result = await sender.Send(new RenameFileCommand(id, preview), ct);

        if (!result.IsSuccess)
        {
            var error = result.Errors.FirstOrDefault() ?? "Unknown error";

            if (error.StartsWith("MEDIAFILE_NOT_FOUND", StringComparison.OrdinalIgnoreCase))
                return NotFound(ApiResponse.Fail(new ApiError("MEDIAFILE_NOT_FOUND",
                    $"MediaFile '{id}' was not found.")));

            if (error.StartsWith("TMDB_ASSIGNMENT_REQUIRED", StringComparison.OrdinalIgnoreCase))
                return UnprocessableEntity(ApiResponse.Fail(new ApiError("TMDB_ASSIGNMENT_REQUIRED",
                    "This file has no TMDB assignment. Reassign TMDB before renaming.")));

            if (error.StartsWith("EPISODE_TITLE_NOT_AVAILABLE", StringComparison.OrdinalIgnoreCase))
                return UnprocessableEntity(ApiResponse.Fail(new ApiError("EPISODE_TITLE_NOT_AVAILABLE",
                    "Episode title not available — run TMDB enrichment first.")));

            if (error.StartsWith("FILE_CONFLICT", StringComparison.OrdinalIgnoreCase))
                return UnprocessableEntity(ApiResponse.Fail(new ApiError("FILE_CONFLICT",
                    ExtractMessage(error))));

            if (error.StartsWith("FILE_NOT_FOUND", StringComparison.OrdinalIgnoreCase))
                return NotFound(ApiResponse.Fail(new ApiError("FILE_NOT_FOUND",
                    ExtractMessage(error))));

            return BadRequest(ApiResponse.Fail(new ApiError("VALIDATION_ERROR", error)));
        }

        return Ok(ApiResponse<FileRenameResultDto>.Success(result.Value));
    }

    private static string ExtractMessage(string error)
    {
        var colonIdx = error.IndexOf(':');
        return colonIdx >= 0 && colonIdx < error.Length - 2
            ? error[(colonIdx + 2)..]
            : error;
    }
}

