using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Admin.Queries;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediaHandler.API.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize(Policy = "AdminOnly")]
public class AdminController : ControllerBase
{
    private readonly ISender _sender;

    public AdminController(ISender sender) => _sender = sender;

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null, CancellationToken ct = default)
    {
        var result = await _sender.Send(new GetUsersQuery(page, pageSize, search), ct);
        var meta = new ApiResponseMeta(result.Value.Page, result.Value.PageSize, result.Value.TotalCount, result.Value.TotalPages);
        return Ok(ApiResponse<object>.Success(result.Value.Items, meta));
    }

    [HttpPut("users/{userId:guid}/role")]
    public async Task<IActionResult> SetRole(Guid userId, [FromBody] SetRoleRequest request, CancellationToken ct)
    {
        await _sender.Send(new SetUserRoleCommand(userId, request.Role), ct);
        return NoContent();
    }

    [HttpPut("users/{userId:guid}/active")]
    public async Task<IActionResult> SetActive(Guid userId, [FromBody] SetActiveRequest request, CancellationToken ct)
    {
        await _sender.Send(new SetUserActiveCommand(userId, request.IsActive), ct);
        return NoContent();
    }
}

public record SetRoleRequest(UserRole Role);
public record SetActiveRequest(bool IsActive);
