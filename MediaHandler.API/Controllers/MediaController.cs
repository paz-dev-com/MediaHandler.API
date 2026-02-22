using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Media.Commands.CreateMedia;
using MediaHandler.Application.Features.Media.Commands.DeleteMedia;
using MediaHandler.Application.Features.Media.Queries.GetMediaById;
using MediaHandler.Application.Features.Media.Queries.GetMediaList;
using MediaHandler.Application.Features.WatchStatus.Commands.SetWatchStatus;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediaHandler.API.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class MediaController : ControllerBase
{
    private readonly ISender _sender;

    public MediaController(ISender sender) => _sender = sender;

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] MediaType? type = null,
        [FromQuery] bool? isWatched = null,
        CancellationToken ct = default)
    {
        var result = await _sender.Send(new GetMediaListQuery(page, pageSize, search, type, isWatched), ct);
        var meta = new ApiResponseMeta(result.Value.Page, result.Value.PageSize, result.Value.TotalCount, result.Value.TotalPages);
        return Ok(ApiResponse<object>.Success(result.Value.Items, meta));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var result = await _sender.Send(new GetMediaByIdQuery(id), ct);
        return result.IsSuccess
            ? Ok(ApiResponse<object>.Success(result.Value))
            : NotFound();
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateMediaCommand command, CancellationToken ct)
    {
        var result = await _sender.Send(command, ct);
        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { id = result.Value }, ApiResponse<object>.Success(new { id = result.Value }))
            : BadRequest(ApiResponse<object>.Fail(result.Errors.Select(e => new ApiError("ERROR", e)).ToArray()));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _sender.Send(new DeleteMediaCommand(id), ct);
        return NoContent();
    }

    [HttpPut("{id:guid}/watched")]
    public async Task<IActionResult> SetWatched(Guid id, [FromBody] SetWatchedRequest request, CancellationToken ct)
    {
        await _sender.Send(new SetWatchStatusCommand(id, request.IsWatched), ct);
        return NoContent();
    }
}

public record SetWatchedRequest(bool IsWatched);
