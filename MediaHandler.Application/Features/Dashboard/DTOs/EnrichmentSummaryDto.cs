namespace MediaHandler.Application.Features.Dashboard.DTOs;

/// <summary>
///     Pre-flight summary showing how many media entries would be processed by an enrichment run.
/// </summary>
public record EnrichmentSummaryDto(
    int NewCount,
    int ChangedCount,
    int SkippedCount,
    int TotalEligible);
