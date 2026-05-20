using MediaHandler.API.Contracts.Admin;
using MediaHandler.API.Models;
using MediaHandler.Application.Features.Admin.Commands.SetUserActive;
using MediaHandler.Application.Features.Admin.Commands.SetUserRole;
using MediaHandler.Application.Features.Admin.Queries.GetUsers;
using MediaHandler.Application.Features.Auth.DTOs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MediaHandler.API.Controllers;

[ApiController]
[Route("api/v1/[controller]/users")]
[Authorize(Policy = "AdminOnly")]
[EnableRateLimiting("fixed")]
public class AdminController(ISender sender) : ControllerBase
{
    [HttpGet("")]
    [ProducesResponseType<ApiResponse<IEnumerable<UserDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetUsers([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null, [FromQuery] string? sortField = null,
        [FromQuery] string? sortOrder = "asc", CancellationToken ct = default)
    {
        var result = await sender.Send(new GetUsersQuery(page, pageSize, search, sortField, sortOrder), ct);
        var meta = new ApiResponseMeta(result.Value.Page, result.Value.PageSize, result.Value.TotalCount,
            result.Value.TotalPages);
        return Ok(ApiResponse<object>.Success(result.Value.Items, meta));
    }

    [HttpPut("{userId:guid}/role")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SetRole(Guid userId, [FromBody] SetRoleRequest request, CancellationToken ct)
    {
        var result = await sender.Send(new SetUserRoleCommand(userId, request.Role), ct);
        return result.IsSuccess
            ? NoContent()
            : NotFound(ApiResponse.Fail(result.Errors.Select(e => new ApiError("NOT_FOUND", e)).ToArray()));
    }

    [HttpPut("{userId:guid}/active")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SetActive(Guid userId, [FromBody] SetActiveRequest request, CancellationToken ct)
    {
        var result = await sender.Send(new SetUserActiveCommand(userId, request.IsActive), ct);
        return result.IsSuccess
            ? NoContent()
            : NotFound(ApiResponse.Fail(result.Errors.Select(e => new ApiError("NOT_FOUND", e)).ToArray()));
    }
}