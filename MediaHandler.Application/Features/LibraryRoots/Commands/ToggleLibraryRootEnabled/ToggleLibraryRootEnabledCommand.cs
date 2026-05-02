using FluentValidation;
using MediaHandler.Application.Common.DTOs;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.LibraryRoots.Commands.ToggleLibraryRootEnabled;

public record ToggleLibraryRootEnabledCommand(Guid Id, bool IsEnabled) : IRequest<Result<LibraryRootDto>>;

public class ToggleLibraryRootEnabledCommandValidator : AbstractValidator<ToggleLibraryRootEnabledCommand>
{
    public ToggleLibraryRootEnabledCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}

public sealed class ToggleLibraryRootEnabledCommandHandler(IApplicationDbContext db)
    : IRequestHandler<ToggleLibraryRootEnabledCommand, Result<LibraryRootDto>>
{
    public async Task<Result<LibraryRootDto>> Handle(
        ToggleLibraryRootEnabledCommand request,
        CancellationToken cancellationToken)
    {
        var root = await db.LibraryRoots
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);

        if (root is null)
            return Result.Fail<LibraryRootDto>("NOT_FOUND: Library root not found.");

        // Guard: cannot toggle while a scan is running that targets this root
        var activeScan = await db.ScanRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Status == ScanStatus.Running, cancellationToken);

        if (activeScan is not null)
            if (activeScan.LibraryRootIdsJson.Contains(request.Id.ToString(), StringComparison.OrdinalIgnoreCase)
                || activeScan.LibraryRootIdsJson == "[]") // empty means ALL roots → conflict
                return Result.Fail<LibraryRootDto>("SCAN_IN_PROGRESS: Cannot toggle a library root while a scan is running.");

        root.IsEnabled = request.IsEnabled;
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new LibraryRootDto(
            root.Id,
            root.Path,
            root.Kind,
            root.Label,
            root.IsEnabled,
            root.CreatedAt,
            root.UpdatedAt));
    }
}

