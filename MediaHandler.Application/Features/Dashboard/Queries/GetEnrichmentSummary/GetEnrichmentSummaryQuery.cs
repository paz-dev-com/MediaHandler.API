using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Dashboard.DTOs;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Dashboard.Queries.GetEnrichmentSummary;

/// <summary>Query to retrieve a pre-flight enrichment summary.</summary>
public record GetEnrichmentSummaryQuery : IRequest<Result<EnrichmentSummaryDto>>;

/// <summary>
///     Returns counts of media entries that would be processed by a new enrichment run:
///     new (never enriched), changed (updated since last enrichment), and skipped (already up-to-date).
/// </summary>
public sealed class GetEnrichmentSummaryQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetEnrichmentSummaryQuery, Result<EnrichmentSummaryDto>>
{
    public async Task<Result<EnrichmentSummaryDto>> Handle(
        GetEnrichmentSummaryQuery request,
        CancellationToken cancellationToken)
    {
        // Last completed enrichment timestamp
        var lastFinishedAt = await db.EnrichmentRuns
            .AsNoTracking()
            .Where(r => r.Status == EnrichmentStatus.Completed && r.FinishedAt.HasValue)
            .OrderByDescending(r => r.FinishedAt)
            .Select(r => r.FinishedAt)
            .FirstOrDefaultAsync(cancellationToken);

        // New: has a TMDB assignment but never enriched (Overview IS NULL)
        var newCount = await db.Medias
            .AsNoTracking()
            .Where(m => m.TmdbId > 0 && m.Overview == null)
            .CountAsync(cancellationToken);

        // Changed: enriched before but updated since last enrichment
        var changedCount = 0;
        if (lastFinishedAt.HasValue)
        {
            changedCount = await db.Medias
                .AsNoTracking()
                .Where(m => m.TmdbId > 0
                            && m.Overview != null
                            && m.UpdatedAt > lastFinishedAt.Value)
                .CountAsync(cancellationToken);
        }

        // Total media with TMDB assignment
        var totalAssigned = await db.Medias
            .AsNoTracking()
            .Where(m => m.TmdbId > 0)
            .CountAsync(cancellationToken);

        var totalEligible = newCount + changedCount;
        var skippedCount = totalAssigned - totalEligible;

        return Result.Success(new EnrichmentSummaryDto(
            NewCount: newCount,
            ChangedCount: changedCount,
            SkippedCount: skippedCount,
            TotalEligible: totalEligible));
    }
}


