using System.Text.Json;
using MediaHandler.API.Contracts.Admin;
using MediaHandler.API.Models;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Common.Models.Kodi;
using MediaHandler.Application.Features.KodiImport.Commands.StartKodiImport;
using MediaHandler.Application.Features.KodiImport.DTOs;
using MediaHandler.Application.Features.KodiImport.Queries.GetActiveKodiImport;
using MediaHandler.Application.Features.KodiImport.Queries.GetKodiImportRun;
using MediaHandler.Application.Features.KodiImport.Queries.ListKodiImportHistory;
using MediaHandler.Application.Features.KodiImport.Queries.ListKodiImportItems;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using System.IO;

namespace MediaHandler.API.Controllers;

/// <summary>
///     Admin endpoints for Kodi video database imports (upload, preview, run reports).
///     All endpoints require the <c>AdminOnly</c> policy.
/// </summary>
[ApiController]
[Route("api/v1/admin/kodi-import")]
[Authorize(Policy = "AdminOnly")]
[EnableRateLimiting("fixed")]
public class AdminKodiImportController(ISender sender, ILogger<AdminKodiImportController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions OverrideJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    ///     Upload a Kodi video database (<c>MyVideos&lt;version&gt;.db</c>) and start an import run.
    ///     Returns 202 Accepted with the run summary; the import executes in the background —
    ///     poll <c>GET /kodi-import/{id}</c> for progress and the final report.
    ///     Pass <c>mode=preview</c> for a dry run that persists no domain data and performs no
    ///     provider traffic. <c>overrides</c> is an optional JSON array of
    ///     <c>[{"kodiPrefix":…,"nasPrefix":…}]</c> prepended to the persisted mappings.
    ///     Returns 409 Conflict when another import is already running.
    /// </summary>
    [HttpPost("")]
    [RequestSizeLimit(524_288_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 524_288_000)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<ApiResponse<ImportRunDto>>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> StartImport(
        IFormFile? file,
        [FromForm] string? mode,
        [FromForm] string? overrides,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            logger.LogWarning("Rejected Kodi import request: no file was provided.");
            return BadRequest(ApiResponse.Fail(new ApiError(
                "VALIDATION_ERROR", "A non-empty Kodi database file is required.", "file")));
        }

        if (!TryParseMode(mode, out var importMode))
        {
            logger.LogWarning("Rejected Kodi import request for file {FileName}: invalid mode {Mode}.", file.FileName, mode);
            return BadRequest(ApiResponse.Fail(new ApiError(
                "VALIDATION_ERROR", "mode must be 'import' or 'preview'.", "mode")));
        }

        if (!TryParseOverrides(overrides, file.FileName, out var overrideMappings, out var overrideError))
        {
            return overrideError!;
        }

        await using var content = file.OpenReadStream();
        return await StartImportInternal(file.FileName, file.Length, content, importMode, overrideMappings, ct);
    }

    /// <summary>
    ///     Raw binary upload alternative for environments where multipart uploads are
    ///     truncated by intermediate proxies. The request body is the DB file bytes.
    /// </summary>
    [HttpPost("raw")]
    [RequestSizeLimit(524_288_000)]
    [Consumes("application/octet-stream")]
    [ProducesResponseType<ApiResponse<ImportRunDto>>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> StartImportRaw(
        [FromQuery] string? fileName,
        [FromQuery] string? mode,
        [FromQuery] string? overrides,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return BadRequest(ApiResponse.Fail(new ApiError(
                "VALIDATION_ERROR", "fileName query parameter is required.", "fileName")));
        }

        if (!TryParseMode(mode, out var importMode))
        {
            logger.LogWarning("Rejected raw Kodi import request for file {FileName}: invalid mode {Mode}.", fileName, mode);
            return BadRequest(ApiResponse.Fail(new ApiError(
                "VALIDATION_ERROR", "mode must be 'import' or 'preview'.", "mode")));
        }

        if (!TryParseOverrides(overrides, fileName, out var overrideMappings, out var overrideError))
        {
            return overrideError!;
        }

        try
        {
            var declaredLength = Request.ContentLength ?? 0;
            return await StartImportInternal(fileName, declaredLength, Request.Body, importMode, overrideMappings, ct);
        }
        catch (IOException ioEx)
        {
            logger.LogWarning(ioEx,
                "Raw Kodi import upload for file {FileName} failed while reading request body.", fileName);
            return BadRequest(ApiResponse.Fail(new ApiError(
                "UPLOAD_INCOMPLETE",
                "The uploaded request body ended unexpectedly. Please retry the upload.")));
        }
    }

    private async Task<IActionResult> StartImportInternal(
        string fileName,
        long declaredLength,
        Stream content,
        KodiImportMode importMode,
        IReadOnlyList<KodiPathMappingSnapshot>? overrideMappings,
        CancellationToken ct)
    {
        try
        {
            var result = await sender.Send(
                new StartKodiImportCommand(fileName, declaredLength, content, importMode, overrideMappings), ct);

            if (!result.IsSuccess)
            {
                var error = result.Errors.FirstOrDefault() ?? "Unknown error";
                logger.LogWarning("Kodi import request for file {FileName} was rejected: {Error}.", fileName, error);
                return MapStartError(error);
            }

            // Re-read the newly created run to build the response (mirrors AdminScanController.StartScan)
            var detail = await sender.Send(new GetKodiImportRunQuery(result.Value.ImportRunId), ct);
            if (detail.IsSuccess)
            {
                var d = detail.Value;
                logger.LogInformation("Accepted Kodi import run {ImportRunId} for file {FileName} (mode={Mode}).",
                    result.Value.ImportRunId, fileName, importMode);
                return StatusCode(StatusCodes.Status202Accepted,
                    ApiResponse<ImportRunDto>.Success(new ImportRunDto(
                        d.Id, d.Mode, d.Status, d.SourceFileName, d.SchemaVersion,
                        d.StartedAt, d.FinishedAt, d.FailureReason, d.Counts)));
            }

            logger.LogInformation("Accepted Kodi import run {ImportRunId} for file {FileName} (mode={Mode}); run details were not available yet.",
                result.Value.ImportRunId, fileName, importMode);
            return StatusCode(StatusCodes.Status202Accepted,
                ApiResponse<ImportRunDto>.Success(new ImportRunDto(
                    result.Value.ImportRunId,
                    importMode,
                    ImportRunStatus.Pending,
                    fileName,
                    0,
                    DateTime.UtcNow,
                    null,
                    null,
                    new ImportCountsDto(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0))));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected failure while starting Kodi import for file {FileName}.", fileName);
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse.Fail(new ApiError("INTERNAL_ERROR", "The Kodi import could not be started.")));
        }
    }

    private static bool TryParseMode(string? mode, out KodiImportMode importMode)
    {
        if (string.IsNullOrWhiteSpace(mode) || mode.Equals("import", StringComparison.OrdinalIgnoreCase))
        {
            importMode = KodiImportMode.Import;
            return true;
        }

        if (mode.Equals("preview", StringComparison.OrdinalIgnoreCase))
        {
            importMode = KodiImportMode.Preview;
            return true;
        }

        importMode = KodiImportMode.Import;
        return false;
    }

    private bool TryParseOverrides(
        string? overrides,
        string fileName,
        out IReadOnlyList<KodiPathMappingSnapshot>? overrideMappings,
        out IActionResult? error)
    {
        overrideMappings = null;
        error = null;

        if (string.IsNullOrWhiteSpace(overrides))
        {
            return true;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<PathMappingOverrideRequest>>(overrides, OverrideJsonOptions);
            if (parsed is not null)
            {
                overrideMappings = parsed
                    .Select(o => new KodiPathMappingSnapshot(o.KodiPrefix ?? string.Empty, o.NasPrefix ?? string.Empty))
                    .ToList();
            }

            return true;
        }
        catch (JsonException)
        {
            logger.LogWarning("Rejected Kodi import request for file {FileName}: malformed overrides JSON.", fileName);
            error = BadRequest(ApiResponse.Fail(new ApiError(
                "VALIDATION_ERROR",
                "overrides must be a JSON array of {\"kodiPrefix\":…,\"nasPrefix\":…}.",
                "overrides")));
            return false;
        }
    }

    /// <summary>Browse the import-run history, newest first. Paginated (max pageSize 100).</summary>
    [HttpGet("")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<ImportRunDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListHistory(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await sender.Send(new ListKodiImportHistoryQuery(page, pageSize), ct);

        if (!result.IsSuccess)
        {
            return BadRequest(ApiResponse.Fail(new ApiError("VALIDATION_ERROR",
                result.Errors.FirstOrDefault() ?? "Invalid query parameters.")));
        }

        var paged = result.Value;
        var meta = new ApiResponseMeta(paged.Page, paged.PageSize, paged.TotalCount, paged.TotalPages);

        return Ok(ApiResponse<IReadOnlyList<ImportRunDto>>.Success(paged.Items, meta));
    }

    /// <summary>Returns the currently running import, or a 200 response with null data when idle.</summary>
    [HttpGet("active")]
    [ProducesResponseType<ApiResponse<ImportRunDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetActive(CancellationToken ct)
    {
        var result = await sender.Send(new GetActiveKodiImportQuery(), ct);
        return Ok(ApiResponse<ImportRunDto>.Success(result.Value!));
    }

    /// <summary>Fetch a single import run with its summary counters and unmatched path prefixes.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ApiResponse<ImportRunDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRun(Guid id, CancellationToken ct)
    {
        var result = await sender.Send(new GetKodiImportRunQuery(id), ct);

        if (!result.IsSuccess)
        {
            return NotFound(ApiResponse.Fail(new ApiError("NOT_FOUND",
                $"Import run '{id}' was not found.")));
        }

        return Ok(ApiResponse<ImportRunDetailDto>.Success(result.Value));
    }

    /// <summary>
    ///     Browse the per-item outcomes of an import run, with optional <c>outcome</c> and
    ///     <c>kind</c> filters. Paginated (max pageSize 100).
    /// </summary>
    [HttpGet("{id:guid}/items")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<ImportItemOutcomeDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListItems(
        Guid id,
        [FromQuery] ImportItemStatus? outcome = null,
        [FromQuery] KodiItemKind? kind = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var result = await sender.Send(new ListKodiImportItemsQuery(id, outcome, kind, page, pageSize), ct);

        if (!result.IsSuccess)
        {
            var error = result.Errors.FirstOrDefault() ?? string.Empty;
            if (error.StartsWith("NOT_FOUND", StringComparison.OrdinalIgnoreCase))
                return NotFound(ApiResponse.Fail(new ApiError("NOT_FOUND", error)));

            return BadRequest(ApiResponse.Fail(new ApiError("VALIDATION_ERROR",
                string.IsNullOrEmpty(error) ? "Invalid query parameters." : error)));
        }

        var paged = result.Value;
        var meta = new ApiResponseMeta(paged.Page, paged.PageSize, paged.TotalCount, paged.TotalPages);

        return Ok(ApiResponse<IReadOnlyList<ImportItemOutcomeDto>>.Success(paged.Items, meta));
    }

    private IActionResult MapStartError(string error)
    {
        if (error.StartsWith("IMPORT_IN_PROGRESS", StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(ApiResponse.Fail(new ApiError("IMPORT_IN_PROGRESS",
                "An import is already running. Wait for it to complete.")));
        }

        var code = error switch
        {
            var e when e.StartsWith("INVALID_FILE_NAME", StringComparison.OrdinalIgnoreCase) => "INVALID_FILE_NAME",
            var e when e.StartsWith("UNSUPPORTED_VERSION", StringComparison.OrdinalIgnoreCase) => "UNSUPPORTED_VERSION",
            var e when e.StartsWith("UPLOAD_TOO_LARGE", StringComparison.OrdinalIgnoreCase) => "UPLOAD_TOO_LARGE",
            var e when e.StartsWith("INVALID_KODI_DB", StringComparison.OrdinalIgnoreCase) => "INVALID_KODI_DB",
            _ => "VALIDATION_ERROR"
        };

        return BadRequest(ApiResponse.Fail(new ApiError(code, error)));
    }
}
