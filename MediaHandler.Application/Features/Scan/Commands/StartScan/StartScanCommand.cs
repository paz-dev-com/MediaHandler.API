using FluentValidation;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Common.Models.Scanner;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Scan.Commands.StartScan;

public record StartScanCommand(
    Guid[] LibraryRootIds,
    ScanMode Mode) : IRequest<Result<ScanRunHandle>>;

public class StartScanCommandValidator : AbstractValidator<StartScanCommand>
{
    public StartScanCommandValidator()
    {
        RuleFor(x => x.LibraryRootIds)
            .Must(ids => ids.Distinct().Count() == ids.Length)
            .WithMessage("LibraryRootIds must be distinct.");

        RuleFor(x => x.Mode)
            .IsInEnum().WithMessage("Mode must be Full or Incremental.");
    }
}

public sealed class StartScanCommandHandler(
    IApplicationDbContext db,
    IScanRunCoordinator coordinator)
    : IRequestHandler<StartScanCommand, Result<ScanRunHandle>>
{
    public async Task<Result<ScanRunHandle>> Handle(
        StartScanCommand request,
        CancellationToken cancellationToken)
    {
        // Single-active-scan guard
        var activeScan = await db.ScanRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Status == ScanStatus.Running, cancellationToken);

        if (activeScan is not null)
            return Result.Fail<ScanRunHandle>(
                "SCAN_IN_PROGRESS: A scan is already running. Wait for it to complete or cancel it.");

        // Resolve library roots
        Guid[] rootIds;
        if (request.LibraryRootIds.Length == 0)
        {
            // Empty array → scan ALL enabled roots
            rootIds = await db.LibraryRoots
                .AsNoTracking()
                .Where(r => r.IsEnabled)
                .Select(r => r.Id)
                .ToArrayAsync(cancellationToken);
        }
        else
        {
            rootIds = request.LibraryRootIds;

            // Validate that all supplied ids exist and are enabled
            var validRoots = await db.LibraryRoots
                .AsNoTracking()
                .Where(r => rootIds.Contains(r.Id))
                .ToListAsync(cancellationToken);

            if (validRoots.Count != rootIds.Length)
                return Result.Fail<ScanRunHandle>("One or more library root ids do not exist.");

            var disabledRoots = validRoots.Where(r => !r.IsEnabled).ToList();
            if (disabledRoots.Count > 0)
                return Result.Fail<ScanRunHandle>(
                    $"Library roots [{string.Join(", ", disabledRoots.Select(r => r.Id))}] are disabled.");
        }

        if (rootIds.Length == 0)
            return Result.Fail<ScanRunHandle>("No enabled library roots found to scan.");

        // Delegate to coordinator
        try
        {
            var scanRunId = Guid.NewGuid();
            var handle = await coordinator.StartAsync(
                new ScanStartParameters(scanRunId, rootIds, request.Mode),
                cancellationToken);

            return Result.Success(handle);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("SCAN_IN_PROGRESS"))
        {
            return Result.Fail<ScanRunHandle>("SCAN_IN_PROGRESS: A scan is already running.");
        }
    }
}