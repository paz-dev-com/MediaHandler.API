using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Auth.Commands.SyncUser;
using MediaHandler.Application.Features.Auth.Commands.UpdatePreferences;
using MediaHandler.Application.Features.Auth.Queries.GetCurrentUser;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace MediaHandler.API.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class AuthController : ControllerBase
{
    private readonly ISender _sender;

    public AuthController(ISender sender) => _sender = sender;

    [HttpPost("sync")]
    public async Task<IActionResult> Sync(CancellationToken ct)
    {
        var oktaId = User.FindFirstValue("sub")!;
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email")!;
        var name = User.FindFirstValue("name");

        var result = await _sender.Send(new SyncUserCommand(oktaId, email, name), ct);
        return result.IsSuccess
            ? Ok(ApiResponse<object>.Success(result.Value))
            : BadRequest(ApiResponse<object>.Fail(result.Errors.Select(e => new ApiError("ERROR", e)).ToArray()));
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var result = await _sender.Send(new GetCurrentUserQuery(), ct);
        return result.IsSuccess
            ? Ok(ApiResponse<object>.Success(result.Value))
            : NotFound(ApiResponse<object>.Fail(new ApiError("NOT_FOUND", result.Errors.FirstOrDefault() ?? "User not found")));
    }

    [HttpPut("preferences")]
    public async Task<IActionResult> UpdatePreferences([FromBody] UpdatePreferencesRequest request, CancellationToken ct)
    {
        var result = await _sender.Send(new UpdatePreferencesCommand(request.PreferredLanguage), ct);
        return result.IsSuccess
            ? Ok(ApiResponse<object>.Success(result.Value))
            : BadRequest(ApiResponse<object>.Fail(result.Errors.Select(e => new ApiError("ERROR", e)).ToArray()));
    }
}

public record UpdatePreferencesRequest(string PreferredLanguage);
