using MediaHandler.API.Contracts.Episodes;
using MediaHandler.API.Models;
using MediaHandler.Application.Features.Episodes.Commands.SetEpisodeWatched;
using MediaHandler.Application.Features.Episodes.DTOs;
using MediaHandler.Application.Features.Episodes.Queries.GetSeasons;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MediaHandler.API.Controllers;

[ApiController]
[Route("api/v1/media/{mediaId:guid}/seasons")]
[Authorize]
[EnableRateLimiting("fixed")]
public class EpisodesController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiResponse<IEnumerable<TvSeasonDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetSeasons(Guid mediaId, CancellationToken ct)
    {
        var result = await sender.Send(new GetSeasonsQuery(mediaId), ct);
        return Ok(ApiResponse<object>.Success(result.Value));
    }

    [HttpPut("{seasonId:guid}/episodes/{episodeId:guid}/watched")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SetEpisodeWatched(Guid episodeId, [FromBody] SetEpisodeWatchedRequest request,
        CancellationToken ct, Guid mediaId, Guid seasonId)
    {
        var result = await sender.Send(new SetEpisodeWatchedCommand(episodeId, request.IsWatched), ct);
        return result.IsSuccess
            ? NoContent()
            : NotFound(ApiResponse.Fail(result.Errors.Select(e => new ApiError("NOT_FOUND", e)).ToArray()));
    }
}