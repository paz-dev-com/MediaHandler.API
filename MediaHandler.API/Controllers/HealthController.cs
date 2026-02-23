using MediaHandler.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MediaHandler.API.Controllers;

public record HealthResponse(string Status, DateTime Timestamp, string Version);

[ApiController]
[Route("api/v1/[controller]")]
public class HealthController : ControllerBase
{
    private readonly HealthCheckService _healthCheckService;

    public HealthController(HealthCheckService healthCheckService)
        => _healthCheckService = healthCheckService;

    [HttpGet]
    [ProducesResponseType<ApiResponse<HealthResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<HealthResponse>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var report = await _healthCheckService.CheckHealthAsync(ct);
        var status = report.Status == HealthStatus.Healthy ? "Healthy" : "Unhealthy";
        var response = ApiResponse<HealthResponse>.Success(new HealthResponse(status, DateTime.UtcNow, "1.0.0"));

        return report.Status == HealthStatus.Healthy ? Ok(response) : StatusCode(StatusCodes.Status503ServiceUnavailable, response);
    }
}
