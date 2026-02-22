using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Episodes.Commands.SetEpisodeWatched;
using MediaHandler.Application.Features.Episodes.Queries.GetSeasons;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediaHandler.API.Controllers;

[ApiController]
[Route("api/v1/media/{mediaId:guid}/seasons")]
[Authorize]
public class EpisodesController : ControllerBase
{
    private readonly ISender _sender;

    public EpisodesController(ISender sender) => _sender = sender;

    [HttpGet]
    public async Task<IActionResult> GetSeasons(Guid mediaId, CancellationToken ct)
    {
        var result = await _sender.Send(new GetSeasonsQuery(mediaId), ct);
        return Ok(ApiResponse<object>.Success(result.Value));
    }

    [HttpPut("{seasonId:guid}/episodes/{episodeId:guid}/watched")]
    public async Task<IActionResult> SetEpisodeWatched(Guid episodeId, [FromBody] SetEpisodeWatchedRequest request, CancellationToken ct)
    {
        await _sender.Send(new SetEpisodeWatchedCommand(episodeId, request.IsWatched), ct);
        return NoContent();
    }
}

public record SetEpisodeWatchedRequest(bool IsWatched);
