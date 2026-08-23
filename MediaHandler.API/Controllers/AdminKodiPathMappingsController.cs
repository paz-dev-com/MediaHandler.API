using MediaHandler.API.Contracts.Admin;
using MediaHandler.API.Models;
using MediaHandler.Application.Features.KodiImport.Commands.CreateKodiPathMapping;
using MediaHandler.Application.Features.KodiImport.Commands.DeleteKodiPathMapping;
using MediaHandler.Application.Features.KodiImport.Commands.UpdateKodiPathMapping;
using MediaHandler.Application.Features.KodiImport.DTOs;
using MediaHandler.Application.Features.KodiImport.Queries.ListKodiPathMappings;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MediaHandler.API.Controllers;

/// <summary>
///     Admin endpoints for managing the persisted Kodi → NAS path prefix mappings.
///     All endpoints require the <c>AdminOnly</c> policy.
/// </summary>
[ApiController]
[Route("api/v1/admin/kodi-import/path-mappings")]
[Authorize(Policy = "AdminOnly")]
[EnableRateLimiting("fixed")]
public class AdminKodiPathMappingsController(ISender sender) : ControllerBase
{
    /// <summary>List all mappings in evaluation order.</summary>
    [HttpGet("")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<KodiPathMappingDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await sender.Send(new ListKodiPathMappingsQuery(), ct);
        return Ok(ApiResponse<IReadOnlyList<KodiPathMappingDto>>.Success(result.Value));
    }

    /// <summary>Create a mapping. Prefixes are normalized on write; duplicates are rejected.</summary>
    [HttpPost("")]
    [ProducesResponseType<ApiResponse<KodiPathMappingDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create([FromBody] PathMappingUpsertRequest request, CancellationToken ct)
    {
        var result = await sender.Send(
            new CreateKodiPathMappingCommand(request.KodiPrefix, request.NasPrefix, request.SortOrder), ct);

        if (!result.IsSuccess)
            return MapUpsertError(result.Errors.FirstOrDefault() ?? "Unknown error");

        return Ok(ApiResponse<KodiPathMappingDto>.Success(result.Value));
    }

    /// <summary>Update an existing mapping.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<ApiResponse<KodiPathMappingDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(Guid id, [FromBody] PathMappingUpsertRequest request, CancellationToken ct)
    {
        var result = await sender.Send(
            new UpdateKodiPathMappingCommand(id, request.KodiPrefix, request.NasPrefix,
                request.SortOrder ?? 0), ct);

        if (!result.IsSuccess)
            return MapUpsertError(result.Errors.FirstOrDefault() ?? "Unknown error");

        return Ok(ApiResponse<KodiPathMappingDto>.Success(result.Value));
    }

    /// <summary>Delete a mapping.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await sender.Send(new DeleteKodiPathMappingCommand(id), ct);

        if (!result.IsSuccess)
        {
            return NotFound(ApiResponse.Fail(new ApiError("NOT_FOUND",
                $"Kodi path mapping '{id}' was not found.")));
        }

        return Ok(ApiResponse<object>.Success(new { deleted = true }));
    }

    private IActionResult MapUpsertError(string error)
    {
        if (error.StartsWith("DUPLICATE_MAPPING", StringComparison.OrdinalIgnoreCase))
            return UnprocessableEntity(ApiResponse.Fail(new ApiError("DUPLICATE_MAPPING", error)));

        if (error.StartsWith("NOT_FOUND", StringComparison.OrdinalIgnoreCase))
            return NotFound(ApiResponse.Fail(new ApiError("NOT_FOUND", error)));

        return BadRequest(ApiResponse.Fail(new ApiError("VALIDATION_ERROR", error)));
    }
}
