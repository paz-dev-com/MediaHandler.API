using MediaHandler.API.Models;
using MediaHandler.Application.Features.Files.Commands.ScanNas;
using MediaHandler.Application.Features.Files.Queries.GetNasLocations;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MediaHandler.API.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
[EnableRateLimiting("fixed")]
public class FilesController : ControllerBase
{
    private readonly ISender _sender;

    public FilesController(ISender sender) => _sender = sender;

    [HttpGet("locations")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<string>>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetLocations(CancellationToken ct = default)
    {
        var result = await _sender.Send(new GetNasLocationsQuery(), ct);
        return Ok(ApiResponse<object>.Success(result.Value));
    }

    [HttpPost("scan")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType<ApiResponse<ScanNasResult>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Scan([FromQuery] string? basePath = null, CancellationToken ct = default)
    {
        var result = await _sender.Send(new ScanNasCommand(basePath), ct);
        return Ok(ApiResponse<object>.Success(result.Value));
    }
}
