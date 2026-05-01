using MediaHandler.API.Contracts.Wishlist;
using MediaHandler.API.Models;
using MediaHandler.Application.Features.Wishlist.Commands.AddToWishlist;
using MediaHandler.Application.Features.Wishlist.Commands.MarkWishlistAcquired;
using MediaHandler.Application.Features.Wishlist.Commands.RemoveFromWishlist;
using MediaHandler.Application.Features.Wishlist.DTOs;
using MediaHandler.Application.Features.Wishlist.Queries.GetWishlist;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MediaHandler.API.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
[EnableRateLimiting("fixed")]
public class WishlistController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiResponse<IEnumerable<WishlistItemDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await sender.Send(new GetWishlistQuery(page, pageSize), ct);
        var meta = new ApiResponseMeta(result.Value.Page, result.Value.PageSize, result.Value.TotalCount,
            result.Value.TotalPages);
        return Ok(ApiResponse<object>.Success(result.Value.Items, meta));
    }

    [HttpPost]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Add([FromBody] AddToWishlistCommand command, CancellationToken ct)
    {
        var result = await sender.Send(command, ct);
        return result.IsSuccess
            ? CreatedAtAction(nameof(List), ApiResponse<object>.Success(new { id = result.Value }))
            : BadRequest(ApiResponse<object>.Fail(result.Errors.Select(e => new ApiError("ERROR", e)).ToArray()));
    }

    [HttpPut("{id:guid}/acquired")]
    [ProducesResponseType<ApiResponse<WishlistItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> MarkAcquired(Guid id, [FromBody] MarkWishlistAcquiredRequest request,
        CancellationToken ct)
    {
        var result = await sender.Send(new MarkWishlistAcquiredCommand(id, request.IsAcquired), ct);
        return result.IsSuccess
            ? Ok(ApiResponse<object>.Success(result.Value))
            : NotFound(ApiResponse.Fail(result.Errors.Select(e => new ApiError("NOT_FOUND", e)).ToArray()));
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Remove(Guid id, CancellationToken ct)
    {
        var result = await sender.Send(new RemoveFromWishlistCommand(id), ct);
        return result.IsSuccess
            ? NoContent()
            : NotFound(ApiResponse.Fail(result.Errors.Select(e => new ApiError("NOT_FOUND", e)).ToArray()));
    }
}