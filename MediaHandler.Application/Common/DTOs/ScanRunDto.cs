using MediaHandler.Domain.Enums;

namespace MediaHandler.Application.Common.DTOs;

/// <summary>
///     Denormalised scan counts snapshot attached to both summary and detail DTOs.
/// </summary>
public record ScanCountsDto(
    int TotalDiscovered,
    int Added,
    int Updated,
    int Unchanged,
    int Removed,
    int Excluded,
    int NeedsReview);

/// <summary>
///     Summary of a scan run returned from list / create endpoints (202 response).
/// </summary>
public record ScanRunDto(
    Guid Id,
    ScanMode Mode,
    ScanStatus Status,
    DateTime StartedAt,
    DateTime? FinishedAt,
    string? FailureReason,
    Guid[] LibraryRootIds,
    ScanCountsDto Counts);

/// <summary>
///     Detail view of a scan run including optional open review items.
///     Returned by <c>GetScanRunQuery</c> when <c>IncludeReview</c> is true.
/// </summary>
public record ScanRunDetailDto(
    Guid Id,
    ScanMode Mode,
    ScanStatus Status,
    DateTime StartedAt,
    DateTime? FinishedAt,
    string? FailureReason,
    Guid[] LibraryRootIds,
    ScanCountsDto Counts,
    /// <summary>Open review items for this run; null when <c>includeReview = false</c>.</summary>
    IReadOnlyList<ReviewItemDto>? ReviewItems);