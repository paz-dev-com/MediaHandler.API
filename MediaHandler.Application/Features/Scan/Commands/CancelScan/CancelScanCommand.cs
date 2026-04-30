#nullable enable

using MediaHandler.Application.Common.DTOs;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Scan.Commands.CancelScan;

public record CancelScanCommand(Guid ScanRunId) : IRequest<Result<ScanRunDto>>;

public sealed class CancelScanCommandHandler(
    IApplicationDbContext db,
    IScanRunCoordinator coordinator)
    : IRequestHandler<CancelScanCommand, Result<ScanRunDto>>
{
    public async Task<Result<ScanRunDto>> Handle(
        CancelScanCommand request,
        CancellationToken cancellationToken)
    {
        var run = await db.ScanRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == request.ScanRunId, cancellationToken);

        if (run is null)
            return Result.Fail<ScanRunDto>("Scan run not found.");

        // Idempotent: if already terminal, just return the current state
        if (run.Status is ScanStatus.Completed or ScanStatus.Failed or ScanStatus.Cancelled)
            return Result.Success(MapToDto(run));

        await coordinator.RequestCancellationAsync(request.ScanRunId);

        // Re-read to reflect any status change
        var updated = await db.ScanRuns
            .AsNoTracking()
            .FirstAsync(r => r.Id == request.ScanRunId, cancellationToken);

        return Result.Success(MapToDto(updated));
    }

    private static ScanRunDto MapToDto(Domain.Entities.ScanRun run)
    {
        var rootIds = System.Text.Json.JsonSerializer.Deserialize<Guid[]>(run.LibraryRootIdsJson) ?? [];
        return new ScanRunDto(
            run.Id, run.Mode, run.Status,
            run.StartedAt, run.FinishedAt, run.FailureReason,
            rootIds,
            new ScanCountsDto(run.TotalDiscovered, run.Added, run.Updated,
                run.Unchanged, run.Removed, run.Excluded, run.NeedsReview));
    }
}

