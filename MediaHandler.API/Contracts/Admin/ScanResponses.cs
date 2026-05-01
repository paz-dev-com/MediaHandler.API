using MediaHandler.Application.Common.DTOs;
using MediaHandler.Domain.Enums;

namespace MediaHandler.API.Contracts.Admin;

/// <summary>
///     Lightweight summary returned from the 202, GET /active, and POST /cancel endpoints.
/// </summary>
public record ScanRunSummaryResponse(
    Guid Id,
    ScanMode Mode,
    ScanStatus Status,
    DateTime StartedAt,
    DateTime? FinishedAt,
    Guid[] LibraryRootIds,
    ScanCountsDto Counts);

/// <summary>
///     Full detail response returned from <c>GET /api/v1/admin/scan/{id}</c>.
///     <see cref="ReviewItems" /> is <c>null</c> when <c>includeReview=false</c>;
///     otherwise contains up to 100 open review items for this run.
/// </summary>
public record ScanRunDetailResponse(
    Guid Id,
    ScanMode Mode,
    ScanStatus Status,
    DateTime StartedAt,
    DateTime? FinishedAt,
    string? FailureReason,
    Guid[] LibraryRootIds,
    ScanCountsDto Counts,
    IReadOnlyList<ReviewItemDto>? ReviewItems);