using MediaHandler.Domain.Enums;

namespace MediaHandler.Application.Features.Dashboard.DTOs;

/// <summary>
///     Per-entry error recorded during a batch enrichment run.
/// </summary>
public record EnrichmentErrorDetailDto(
    Guid MediaId,
    int? TmdbId,
    string? Title,
    string Error);

/// <summary>
///     Data-transfer object for a <see cref="MediaHandler.Domain.Entities.EnrichmentRun" /> row,
///     returned by the enrichment status API.
/// </summary>
public record EnrichmentRunDto(
    Guid EnrichmentRunId,
    EnrichmentStatus Status,
    DateTime StartedAt,
    DateTime? FinishedAt,
    int TotalItems,
    int EnrichedCount,
    int FailedCount,
    int SkippedCount,
    string? CurrentItem,
    IReadOnlyList<EnrichmentErrorDetailDto> ErrorDetails);

