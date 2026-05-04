// GetEnrichmentStatus — query and handler that returns the most recent EnrichmentRun row
// mapped to EnrichmentRunDto, or null when no enrichment run has ever been recorded.

using System.Text.Json;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Dashboard.DTOs;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Dashboard.Queries.GetEnrichmentStatus;

/// <summary>Query to retrieve the current enrichment run status.</summary>
public record GetEnrichmentStatusQuery : IRequest<Result<EnrichmentRunDto?>>;

// =========================================================================
// Handler
// =========================================================================

/// <summary>
///     Handles <see cref="GetEnrichmentStatusQuery" />.
///     Returns the most recent <c>EnrichmentRun</c> row mapped to <see cref="EnrichmentRunDto" />,
///     or <c>null</c> when no run has ever been recorded.
/// </summary>
public sealed class GetEnrichmentStatusQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetEnrichmentStatusQuery, Result<EnrichmentRunDto?>>
{
    public async Task<Result<EnrichmentRunDto?>> Handle(
        GetEnrichmentStatusQuery request,
        CancellationToken cancellationToken)
    {
        var run = await db.EnrichmentRuns
            .AsNoTracking()
            .OrderByDescending(r => r.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (run is null)
            return Result.Success<EnrichmentRunDto?>(null);

        IReadOnlyList<EnrichmentErrorDetailDto> errorDetails = [];
        if (!string.IsNullOrEmpty(run.ErrorDetailsJson))
        {
            try
            {
                errorDetails = JsonSerializer.Deserialize<List<EnrichmentErrorDetailDto>>(run.ErrorDetailsJson)
                               ?? [];
            }
            catch
            {
                // Malformed JSON — return empty list rather than failing the request.
                errorDetails = [];
            }
        }

        var dto = new EnrichmentRunDto(
            EnrichmentRunId: run.Id,
            Status: run.Status,
            StartedAt: run.StartedAt,
            FinishedAt: run.FinishedAt,
            TotalItems: run.TotalItems,
            EnrichedCount: run.EnrichedCount,
            FailedCount: run.FailedCount,
            SkippedCount: run.SkippedCount,
            CurrentItem: run.CurrentItem,
            ErrorDetails: errorDetails);

        return Result.Success<EnrichmentRunDto?>(dto);
    }
}

