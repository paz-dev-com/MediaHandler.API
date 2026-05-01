using FluentValidation;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.LibraryRoots.Commands.RemoveLibraryRoot;

public record RemoveLibraryRootCommand(Guid Id) : IRequest<Result>;

public class RemoveLibraryRootCommandValidator : AbstractValidator<RemoveLibraryRootCommand>
{
    public RemoveLibraryRootCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}

public sealed class RemoveLibraryRootCommandHandler(IApplicationDbContext db)
    : IRequestHandler<RemoveLibraryRootCommand, Result>
{
    public async Task<Result> Handle(RemoveLibraryRootCommand request, CancellationToken cancellationToken)
    {
        var root = await db.LibraryRoots
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);

        if (root is null)
            return Result.Fail("Library root not found.");

        // Guard: cannot remove while a scan is running that targets this root
        var activeScan = await db.ScanRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Status == ScanStatus.Running, cancellationToken);

        if (activeScan is not null)
            // Check if this root is referenced in the active scan
            // The LibraryRootIdsJson is an array; check for the id string
            if (activeScan.LibraryRootIdsJson.Contains(request.Id.ToString(), StringComparison.OrdinalIgnoreCase)
                || activeScan.LibraryRootIdsJson == "[]") // empty means ALL roots → conflict
                return Result.Fail("SCAN_IN_PROGRESS: Cannot remove a library root while a scan is running.");

        // Soft-delete cascade: null out LibraryRootId on all MediaFiles, set MissingSince
        var affectedFiles = await db.MediaFiles
            .Where(mf => mf.LibraryRootId == root.Id)
            .ToListAsync(cancellationToken);

        foreach (var file in affectedFiles)
        {
            file.LibraryRootId = null;
            file.MissingSince = DateTime.UtcNow;
        }

        db.LibraryRoots.Remove(root);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}