using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Files.Commands.ScanNas;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediaHandler.API.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class FilesController : ControllerBase
{
    private readonly ISender _sender;

    public FilesController(ISender sender) => _sender = sender;

    [HttpPost("scan")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Scan([FromQuery] string? basePath = null, CancellationToken ct = default)
    {
        var result = await _sender.Send(new ScanNasCommand(basePath), ct);
        return Ok(ApiResponse<object>.Success(result.Value));
    }
}
