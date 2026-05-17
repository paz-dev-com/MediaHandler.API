using System.Text.Json;
using MediaHandler.Application.Common.DTOs;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Scan.Queries.GetScanRun;

/// <summary>
///     Returns the detail view of a specific scan run.
///     When <see cref="IncludeReview" /> is <c>true</c>, attaches up to 100 of the most
///     recent open <c>ReviewItem</c>s that were first seen during this run.
/// </summary>
public record GetScanRunQuery(Guid Id, bool IncludeReview = false)
    : IRequest<Result<ScanRunDetailDto>>;

public sealed class GetScanRunQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetScanRunQuery, Result<ScanRunDetailDto>>
{
    public async Task<Result<ScanRunDetailDto>> Handle(
        GetScanRunQuery request,
        CancellationToken cancellationToken)
    {
        var run = await db.ScanRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);

        if (run is null)
            return Result.Fail<ScanRunDetailDto>($"Scan run '{request.Id}' was not found.");

        var rootIds = JsonSerializer.Deserialize<Guid[]>(run.LibraryRootIdsJson) ?? [];

        IReadOnlyList<ReviewItemDto>? reviewItems = null;
        if (request.IncludeReview)
        {
            var rawItems = await db.ReviewItems
                .AsNoTracking()
                .Where(ri => ri.FirstSeenScanRunId == request.Id && ri.Status == ReviewStatus.Open)
                .OrderByDescending(ri => ri.CreatedAt)
                .Take(100)
                .ToListAsync(cancellationToken);

            reviewItems = rawItems
                .Select(ri => new ReviewItemDto(
                    ri.Id,
                    ri.FilePath,
                    Path.GetDirectoryName(ri.FilePath),
                    ri.Reason,
                    ri.Status,
                    ri.ParsedTitle,
                    ri.ParsedYear,
                    ri.ParsedSeason,
                    ri.ParsedEpisode,
                    new List<TmdbCandidateDto>(), // candidates deserialized separately if needed
                    ri.ResolvedTmdbId,
                    ri.ResolvedKind,
                    ri.ResolvedAt,
                    ri.CreatedAt))
                .ToList();
        }

        return Result.Success(new ScanRunDetailDto(
            run.Id,
            run.Mode,
            run.Status,
            run.StartedAt,
            run.FinishedAt,
            run.FailureReason,
            rootIds,
            new ScanCountsDto(
                run.TotalDiscovered,
                run.Added,
                run.Updated,
                run.Unchanged,
                run.Removed,
                run.Excluded,
                run.NeedsReview),
            reviewItems));
    }
}