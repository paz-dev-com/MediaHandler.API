using MediaHandler.API.Contracts.Media;
using MediaHandler.API.Models;
using MediaHandler.Application.Features.Media.Commands.CreateMedia;
using MediaHandler.Application.Features.Media.Commands.DeleteMedia;
using MediaHandler.Application.Features.Media.DTOs;
using MediaHandler.Application.Features.Media.Queries.GetMediaById;
using MediaHandler.Application.Features.Media.Queries.GetMediaList;
using MediaHandler.Application.Features.Media.Queries.GetMediaStats;
using MediaHandler.Application.Features.WatchStatus.Commands.SetWatchStatus;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MediaHandler.API.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
[EnableRateLimiting("fixed")]
public class MediaController(ISender sender) : ControllerBase
{
    [HttpGet("stats")]
    [ProducesResponseType<ApiResponse<MediaStatsDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Stats(CancellationToken ct)
    {
        var result = await sender.Send(new GetMediaStatsQuery(), ct);
        return Ok(ApiResponse<object>.Success(result.Value));
    }

    [HttpGet]
    [ProducesResponseType<ApiResponse<IEnumerable<MediaListItemDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] MediaType? type = null,
        [FromQuery] bool? isWatched = null,
        [FromQuery] string? genre = null,
        CancellationToken ct = default)
    {
        var result = await sender.Send(new GetMediaListQuery(page, pageSize, search, type, isWatched, genre), ct);
        var meta = new ApiResponseMeta(result.Value.Page, result.Value.PageSize, result.Value.TotalCount, result.Value.TotalPages);
        return Ok(ApiResponse<object>.Success(result.Value.Items, meta));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ApiResponse<MediaDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var result = await sender.Send(new GetMediaByIdQuery(id), ct);
        return result.IsSuccess
            ? Ok(ApiResponse<object>.Success(result.Value))
            : NotFound(ApiResponse.Fail(result.Errors.Select(e => new ApiError("NOT_FOUND", e)).ToArray()));
    }

    [HttpPost]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Create([FromBody] CreateMediaCommand command, CancellationToken ct)
    {
        var result = await sender.Send(command, ct);
        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { id = result.Value }, ApiResponse<object>.Success(new { id = result.Value }))
            : BadRequest(ApiResponse<object>.Fail(result.Errors.Select(e => new ApiError("ERROR", e)).ToArray()));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await sender.Send(new DeleteMediaCommand(id), ct);
        return result.IsSuccess
            ? NoContent()
            : NotFound(ApiResponse.Fail(result.Errors.Select(e => new ApiError("NOT_FOUND", e)).ToArray()));
    }

    [HttpPut("{id:guid}/watched")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SetWatched(Guid id, [FromBody] SetWatchedRequest request, CancellationToken ct)
    {
        var result = await sender.Send(new SetWatchStatusCommand(id, request.IsWatched), ct);
        return result.IsSuccess
            ? NoContent()
            : NotFound(ApiResponse.Fail(result.Errors.Select(e => new ApiError("NOT_FOUND", e)).ToArray()));
    }
}
