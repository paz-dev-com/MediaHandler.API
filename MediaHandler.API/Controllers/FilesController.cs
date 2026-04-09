using MediaHandler.API.Models;
using MediaHandler.Application.Features.Files.Commands.ScanAndImportNas;
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

    /// <summary>
    /// Scans the NAS for new media files, then automatically matches every unlinked
    /// <c>MediaFile</c> against TMDB and imports the corresponding <c>Media</c> entity.
    /// The operation is idempotent: re-running it will not create duplicate records.
    /// </summary>
    [HttpPost("scan-and-import")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType<ApiResponse<ScanAndImportNasResult>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ScanAndImport(
        [FromQuery] string? basePath = null,
        [FromQuery] string? language = null,
        CancellationToken ct = default)
    {
        var result = await _sender.Send(new ScanAndImportNasCommand(basePath, language), ct);
        return Ok(ApiResponse<ScanAndImportNasResult>.Success(result.Value));
    }
}

