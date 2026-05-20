using MediaHandler.API.Contracts.Admin;
using MediaHandler.API.Models;
using MediaHandler.Application.Common.DTOs;
using MediaHandler.Application.Features.LibraryRoots.Commands.AddLibraryRoot;
using MediaHandler.Application.Features.LibraryRoots.Commands.RemoveLibraryRoot;
using MediaHandler.Application.Features.LibraryRoots.Commands.ToggleLibraryRootEnabled;
using MediaHandler.Application.Features.LibraryRoots.Commands.UpdateLibraryRoot;
using MediaHandler.Application.Features.LibraryRoots.Queries.ListLibraryRoots;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MediaHandler.API.Controllers;

/// <summary>
///     Admin CRUD endpoints for NAS library root configuration.
///     All endpoints require the <c>AdminOnly</c> policy.
/// </summary>
[ApiController]
[Route("api/v1/admin/library-roots")]
[Authorize(Policy = "AdminOnly")]
[EnableRateLimiting("fixed")]
public class AdminLibraryRootsController(ISender sender) : ControllerBase
{
    /// <summary>
    ///     List configured library roots, with optional filtering by kind and enabled status.
    /// </summary>
    [HttpGet("")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] LibraryRootKind? kind = null,
        [FromQuery] bool enabledOnly = false,
        [FromQuery] string? sortField = null,
        [FromQuery] string? sortOrder = "asc",
        [FromQuery] string? path = null,
        CancellationToken ct = default)
    {
        var result = await sender.Send(new ListLibraryRootsQuery(page, pageSize, kind, enabledOnly, sortField, sortOrder, path), ct);

        if (!result.IsSuccess)
            return BadRequest(ApiResponse.Fail(result.Errors.Select(e => new ApiError("BAD_REQUEST", e)).ToArray()));

        var meta = new ApiResponseMeta(
            result.Value.Page,
            result.Value.PageSize,
            result.Value.TotalCount,
            result.Value.TotalPages);

        return Ok(ApiResponse<object>.Success(result.Value.Items, meta));
    }

    /// <summary>
    ///     Register a new library root. Returns 201 Created with the persisted root.
    ///     Returns 409 Conflict when the path is already registered.
    /// </summary>
    [HttpPost("")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Add([FromBody] AddLibraryRootRequest request, CancellationToken ct)
    {
        var result = await sender.Send(new AddLibraryRootCommand(request.Path, request.Kind, request.Label), ct);

        if (!result.IsSuccess)
        {
            var error = result.Errors.FirstOrDefault() ?? "Unknown error";

            if (error.Contains("LIBRARY_ROOT_DUPLICATE", StringComparison.OrdinalIgnoreCase))
                return Conflict(ApiResponse.Fail(new ApiError("LIBRARY_ROOT_DUPLICATE",
                    "A library root with this path is already registered.")));

            return BadRequest(ApiResponse.Fail(new ApiError("VALIDATION_ERROR", error)));
        }

        return CreatedAtAction(
            nameof(List),
            null,
            ApiResponse<object>.Success(result.Value));
    }

    /// <summary>
    ///     Remove a library root by id. Existing MediaFile rows are soft-deleted (MissingSince set).
    ///     Returns 409 Conflict when a scan referencing this root is currently running.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Remove(Guid id, CancellationToken ct)
    {
        var result = await sender.Send(new RemoveLibraryRootCommand(id), ct);

        if (!result.IsSuccess)
        {
            var error = result.Errors.FirstOrDefault() ?? "Unknown error";

            if (error.Contains("SCAN_IN_PROGRESS", StringComparison.OrdinalIgnoreCase))
                return Conflict(ApiResponse.Fail(new ApiError("SCAN_IN_PROGRESS",
                    "Cannot remove a library root while a scan targeting it is running.")));

            if (error.Contains("not found", StringComparison.OrdinalIgnoreCase))
                return NotFound(ApiResponse.Fail(new ApiError("NOT_FOUND",
                    $"Library root '{id}' was not found.")));

            return BadRequest(ApiResponse.Fail(new ApiError("BAD_REQUEST", error)));
        }

        return NoContent();
    }

    /// <summary>
    ///     Update the kind and label of an existing library root.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<ApiResponse<LibraryRootDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateLibraryRootRequest request, CancellationToken ct)
    {
        var result = await sender.Send(new UpdateLibraryRootCommand(id, request.Kind, request.Label), ct);

        if (!result.IsSuccess)
        {
            var error = result.Errors.FirstOrDefault() ?? "Unknown error";
            if (error.Contains("NOT_FOUND", StringComparison.OrdinalIgnoreCase))
                return NotFound(ApiResponse.Fail(new ApiError("NOT_FOUND", $"Library root '{id}' was not found.")));
            return BadRequest(ApiResponse.Fail(new ApiError("VALIDATION_ERROR", error)));
        }

        return Ok(ApiResponse<LibraryRootDto>.Success(result.Value));
    }

    /// <summary>
    ///     Enable or disable a library root by id.
    ///     Returns 409 Conflict when a scan referencing this root is currently running.
    /// </summary>
    [HttpPut("{id:guid}/enabled")]
    [ProducesResponseType<ApiResponse<LibraryRootDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ToggleEnabled(Guid id, [FromBody] ToggleLibraryRootEnabledRequest request, CancellationToken ct)
    {
        var result = await sender.Send(new ToggleLibraryRootEnabledCommand(id, request.IsEnabled), ct);

        if (!result.IsSuccess)
        {
            var error = result.Errors.FirstOrDefault() ?? "Unknown error";

            if (error.Contains("NOT_FOUND", StringComparison.OrdinalIgnoreCase))
                return NotFound(ApiResponse.Fail(new ApiError("NOT_FOUND",
                    $"Library root '{id}' was not found.")));

            if (error.Contains("SCAN_IN_PROGRESS", StringComparison.OrdinalIgnoreCase))
                return Conflict(ApiResponse.Fail(new ApiError("SCAN_IN_PROGRESS",
                    "Cannot toggle a library root while a scan targeting it is running.")));

            return BadRequest(ApiResponse.Fail(new ApiError("BAD_REQUEST", error)));
        }

        return Ok(ApiResponse<LibraryRootDto>.Success(result.Value));
    }
}