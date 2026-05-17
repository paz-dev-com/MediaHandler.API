using MediaHandler.API.Models;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Features.Auth.DTOs;
using MediaHandler.Application.Features.Users.Commands.DeleteProfilePicture;
using MediaHandler.Application.Features.Users.Commands.UploadProfilePicture;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MediaHandler.API.Controllers;

/// <summary>
///     Endpoints for user profile management, including profile picture upload, delete, and streaming.
/// </summary>
[ApiController]
[Route("api/v1/users/profile-picture")]
[Authorize]
[EnableRateLimiting("fixed")]
public class UsersController(ISender sender, ICurrentUserService currentUser, IWebHostEnvironment env) : ControllerBase
{
    /// <summary>
    ///     Upload or replace the authenticated user's profile picture.
    ///     Accepts JPEG, PNG, or WebP images up to 2 MB.
    /// </summary>
    [HttpPost("")]
    [ProducesResponseType<ApiResponse<UserDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UploadProfilePicture(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ApiResponse.Fail(new ApiError("VALIDATION_ERROR", "No file provided.")));

        var oktaId = currentUser.OktaId;
        if (string.IsNullOrEmpty(oktaId))
            return Unauthorized();

        var result = await sender.Send(
            new UploadProfilePictureCommand(
                oktaId,
                file.OpenReadStream(),
                file.FileName,
                file.ContentType,
                file.Length),
            ct);

        if (!result.IsSuccess)
        {
            var error = result.Errors.FirstOrDefault() ?? "Unknown error";
            if (error.StartsWith("USER_NOT_FOUND", StringComparison.OrdinalIgnoreCase))
                return NotFound(ApiResponse.Fail(new ApiError("NOT_FOUND", "User not found.")));
            return BadRequest(ApiResponse.Fail(new ApiError("VALIDATION_ERROR", error)));
        }

        return Ok(ApiResponse<UserDto>.Success(result.Value));
    }

    /// <summary>
    ///     Delete the authenticated user's profile picture.
    ///     Returns 404 when the user has no profile picture set.
    /// </summary>
    [HttpDelete("")]
    [ProducesResponseType<ApiResponse<UserDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteProfilePicture(CancellationToken ct)
    {
        var oktaId = currentUser.OktaId;
        if (string.IsNullOrEmpty(oktaId))
            return Unauthorized();

        var result = await sender.Send(new DeleteProfilePictureCommand(oktaId), ct);

        if (!result.IsSuccess)
        {
            var error = result.Errors.FirstOrDefault() ?? "Unknown error";
            if (error.Contains("USER_HAS_NO_PROFILE_PICTURE", StringComparison.OrdinalIgnoreCase))
                return NotFound(ApiResponse.Fail(new ApiError("NOT_FOUND", "User has no profile picture.")));
            if (error.StartsWith("USER_NOT_FOUND", StringComparison.OrdinalIgnoreCase))
                return NotFound(ApiResponse.Fail(new ApiError("NOT_FOUND", "User not found.")));
            return BadRequest(ApiResponse.Fail(new ApiError("VALIDATION_ERROR", error)));
        }

        return Ok(ApiResponse<UserDto>.Success(result.Value));
    }

    /// <summary>
    ///     Stream a profile picture file by filename.
    ///     No authentication required — allows browser image tags to load pictures directly.
    /// </summary>
    [HttpGet("{fileName}")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetProfilePicture(string fileName)
    {
        // Security: reject path traversal attempts
        if (fileName.Contains("..") ||
            fileName.Contains('/') ||
            fileName.Contains('\\'))
            return BadRequest(ApiResponse.Fail(new ApiError("INVALID_FILE_NAME", "Invalid file name.")));

        var webRootPath = env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var filePath = Path.Combine(webRootPath, "uploads", "profile-pictures", fileName);

        if (!System.IO.File.Exists(filePath))
            return NotFound();

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var contentType = ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };

        return PhysicalFile(filePath, contentType);
    }
}

