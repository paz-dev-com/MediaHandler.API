using System.Text.Json;
using MediaHandler.Application.Common.DTOs;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Scan.Queries.GetActiveScan;

/// <summary>
///     Returns the currently running scan, or <c>null</c> in <c>Data</c> if none is active.
///     Always returns HTTP 200.
/// </summary>
public record GetActiveScanQuery : IRequest<Result<ScanRunDto?>>;

public sealed class GetActiveScanQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetActiveScanQuery, Result<ScanRunDto?>>
{
    public async Task<Result<ScanRunDto?>> Handle(
        GetActiveScanQuery request,
        CancellationToken cancellationToken)
    {
        var run = await db.ScanRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Status == ScanStatus.Running, cancellationToken);

        if (run is null)
            return Result.Success<ScanRunDto?>(null);

        var rootIds = JsonSerializer.Deserialize<Guid[]>(run.LibraryRootIdsJson) ?? [];

        return Result.Success<ScanRunDto?>(new ScanRunDto(
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
                run.NeedsReview)));
    }
}