using MediaHandler.API.Contracts.Auth;
using MediaHandler.API.Models;
using MediaHandler.Application.Features.Auth.Commands.SyncUser;
using MediaHandler.Application.Features.Auth.Commands.UpdatePreferences;
using MediaHandler.Application.Features.Auth.DTOs;
using MediaHandler.Application.Features.Auth.Queries.GetCurrentUser;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace MediaHandler.API.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
[EnableRateLimiting("fixed")]
public class AuthController(ISender sender) : ControllerBase
{
    [HttpPost("sync")]
    [ProducesResponseType<ApiResponse<UserDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Sync(CancellationToken ct)
    {
        var oktaId = User.FindFirstValue("sub")!;
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email")!;
        var name = User.FindFirstValue("name");
        var isAdmin = User.IsInRole("Admin");

        var result = await sender.Send(new SyncUserCommand(oktaId, email, name, isAdmin), ct);
        return result.IsSuccess
            ? Ok(ApiResponse<object>.Success(result.Value))
            : BadRequest(ApiResponse<object>.Fail(result.Errors.Select(e => new ApiError("ERROR", e)).ToArray()));
    }

    [HttpGet("me")]
    [ProducesResponseType<ApiResponse<UserDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var result = await sender.Send(new GetCurrentUserQuery(), ct);
        return result.IsSuccess
            ? Ok(ApiResponse<object>.Success(result.Value))
            : NotFound(ApiResponse<object>.Fail(new ApiError("NOT_FOUND", result.Errors.FirstOrDefault() ?? "User not found")));
    }

    [HttpPut("preferences")]
    [ProducesResponseType<ApiResponse<UserDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdatePreferences([FromBody] UpdatePreferencesRequest request, CancellationToken ct)
    {
        var result = await sender.Send(new UpdatePreferencesCommand(request.PreferredLanguage), ct);
        return result.IsSuccess
            ? Ok(ApiResponse<object>.Success(result.Value))
            : BadRequest(ApiResponse<object>.Fail(result.Errors.Select(e => new ApiError("ERROR", e)).ToArray()));
    }
}
