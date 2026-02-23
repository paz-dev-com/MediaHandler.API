using MediaHandler.API.Models;
using MediaHandler.Application.Common.DTOs;
using MediaHandler.Application.Features.Tmdb.Commands.ImportFromTmdb;
using MediaHandler.Application.Features.Tmdb.Queries.SearchTmdb;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MediaHandler.API.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
[EnableRateLimiting("fixed")]
public class TmdbController : ControllerBase
{
    private readonly ISender _sender;

    public TmdbController(ISender sender) => _sender = sender;

    [HttpGet("search")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<TmdbMediaDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Search([FromQuery] string query, [FromQuery] string? language = null, CancellationToken ct = default)
    {
        var result = await _sender.Send(new SearchTmdbQuery(query, language), ct);
        return Ok(ApiResponse<object>.Success(result.Value));
    }

    [HttpPost("import/{tmdbId:int}")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Import(int tmdbId, [FromQuery] string mediaType, [FromQuery] string? language = null, CancellationToken ct = default)
    {
        var result = await _sender.Send(new ImportFromTmdbCommand(tmdbId, mediaType, language), ct);
        return result.IsSuccess
            ? Ok(ApiResponse<object>.Success(new { id = result.Value }))
            : BadRequest(ApiResponse<object>.Fail(result.Errors.Select(e => new ApiError("ERROR", e)).ToArray()));
    }
}
