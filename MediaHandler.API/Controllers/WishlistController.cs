using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Wishlist.Commands;
using MediaHandler.Application.Features.Wishlist.Queries.GetWishlist;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediaHandler.API.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class WishlistController : ControllerBase
{
    private readonly ISender _sender;

    public WishlistController(ISender sender) => _sender = sender;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _sender.Send(new GetWishlistQuery(page, pageSize), ct);
        var meta = new ApiResponseMeta(result.Value.Page, result.Value.PageSize, result.Value.TotalCount, result.Value.TotalPages);
        return Ok(ApiResponse<object>.Success(result.Value.Items, meta));
    }

    [HttpPost]
    public async Task<IActionResult> Add([FromBody] AddToWishlistCommand command, CancellationToken ct)
    {
        var result = await _sender.Send(command, ct);
        return result.IsSuccess
            ? CreatedAtAction(nameof(List), ApiResponse<object>.Success(new { id = result.Value }))
            : BadRequest(ApiResponse<object>.Fail(result.Errors.Select(e => new ApiError("ERROR", e)).ToArray()));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Remove(Guid id, CancellationToken ct)
    {
        await _sender.Send(new RemoveFromWishlistCommand(id), ct);
        return NoContent();
    }
}
