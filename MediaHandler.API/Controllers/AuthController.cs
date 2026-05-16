using System.Security.Claims;
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
    public async Task<IActionResult> Sync([FromBody] SyncUserRequest? body, CancellationToken ct)
    {
        // JWT bearer (production) always provides the authoritative sub.
        // In development with an opaque token (no audience configured), the sub is null
        // so we fall back to the value sent by the frontend from the Auth0 ID token.
        var oktaId = User.FindFirstValue("sub") ?? body?.Sub;
        if (string.IsNullOrEmpty(oktaId))
            return Unauthorized();

        // Auth0 access tokens do NOT include email/name by default — those live in the
        // ID token only. The frontend sends them in the request body (sourced from auth0.user$)
        // as a reliable fallback. JWT claims always take priority when present (e.g. when an
        // Auth0 Action explicitly adds them to the access token).
        var email = User.FindFirstValue(ClaimTypes.Email)
                    ?? User.FindFirstValue("email")
                    ?? User.FindFirstValue("preferred_username")
                    ?? body?.Email;

        if (string.IsNullOrEmpty(email))
            return BadRequest(ApiResponse<object>.Fail(new ApiError("MISSING_CLAIM",
                "Could not determine the user's email address from the token or request body.")));

        // Display name: JWT claims first, then request body fallback
        var name = User.FindFirstValue("name")
                   ?? User.FindFirstValue("given_name")
                   ?? body?.Name;

        // Accept both "Admin" and "Administrator" as admin role names to be resilient
        // to differences in how the role is named in the identity provider.
        // RoleClaimType is configured to "https://mediahandler.com/roles" in JwtBearer options.
        // Fallback: if no Auth0 Action injects roles into the access token, use the roles sent
        // from the frontend (sourced from the Auth0 ID token via auth0.user$).
        var jwtIsAdmin = User.IsInRole("Admin") || User.IsInRole("Administrator");
        var bodyIsAdmin = body?.Roles?.Any(r =>
            string.Equals(r, "Admin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(r, "Administrator", StringComparison.OrdinalIgnoreCase)) ?? false;
        var isAdmin = jwtIsAdmin || bodyIsAdmin;

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
            : NotFound(ApiResponse<object>.Fail(new ApiError("NOT_FOUND",
                result.Errors.FirstOrDefault() ?? "User not found")));
    }

    [HttpPut("preferences")]
    [ProducesResponseType<ApiResponse<UserDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdatePreferences([FromBody] UpdatePreferencesRequest request,
        CancellationToken ct)
    {
        var result = await sender.Send(new UpdatePreferencesCommand(request.PreferredLanguage), ct);
        return result.IsSuccess
            ? Ok(ApiResponse<object>.Success(result.Value))
            : BadRequest(ApiResponse<object>.Fail(result.Errors.Select(e => new ApiError("ERROR", e)).ToArray()));
    }
}