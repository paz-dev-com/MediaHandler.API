using FluentValidation;
using MediaHandler.Application.Common.DTOs;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.LibraryRoots.Commands.UpdateLibraryRoot;

public record UpdateLibraryRootCommand(Guid Id, LibraryRootKind Kind, string? Label)
    : IRequest<Result<LibraryRootDto>>;

public class UpdateLibraryRootCommandValidator : AbstractValidator<UpdateLibraryRootCommand>
{
    public UpdateLibraryRootCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Kind).IsInEnum().WithMessage("Kind must be a valid LibraryRootKind.");
        RuleFor(x => x.Label)
            .MaximumLength(200).WithMessage("Label must not exceed 200 characters.")
            .When(x => x.Label is not null);
    }
}

public sealed class UpdateLibraryRootCommandHandler(IApplicationDbContext db)
    : IRequestHandler<UpdateLibraryRootCommand, Result<LibraryRootDto>>
{
    public async Task<Result<LibraryRootDto>> Handle(
        UpdateLibraryRootCommand request,
        CancellationToken cancellationToken)
    {
        var root = await db.LibraryRoots
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);

        if (root is null)
            return Result.Fail<LibraryRootDto>($"NOT_FOUND: LibraryRoot '{request.Id}' was not found.");

        root.Kind = request.Kind;
        root.Label = request.Label;
        root.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new LibraryRootDto(
            root.Id, root.Path, root.Kind, root.Label, root.IsEnabled, root.CreatedAt, root.UpdatedAt));
    }
}

