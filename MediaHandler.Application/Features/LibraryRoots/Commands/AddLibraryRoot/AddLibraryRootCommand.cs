#nullable enable

using FluentValidation;
using MediaHandler.Application.Common.DTOs;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.LibraryRoots.Commands.AddLibraryRoot;

public record AddLibraryRootCommand(
    string Path,
    LibraryRootKind Kind,
    string? Label) : IRequest<Result<LibraryRootDto>>;

public class AddLibraryRootCommandValidator : AbstractValidator<AddLibraryRootCommand>
{
    public AddLibraryRootCommandValidator()
    {
        RuleFor(x => x.Path)
            .NotEmpty().WithMessage("Path is required.")
            .MaximumLength(1024).WithMessage("Path must not exceed 1024 characters.");

        RuleFor(x => x.Kind)
            .IsInEnum().WithMessage("Kind must be a valid LibraryRootKind.");

        RuleFor(x => x.Label)
            .MaximumLength(200).WithMessage("Label must not exceed 200 characters.")
            .When(x => x.Label is not null);
    }
}

public sealed class AddLibraryRootCommandHandler(
    IApplicationDbContext db,
    INasService nasService)
    : IRequestHandler<AddLibraryRootCommand, Result<LibraryRootDto>>
{
    public async Task<Result<LibraryRootDto>> Handle(
        AddLibraryRootCommand request,
        CancellationToken cancellationToken)
    {
        // Validate path length (defense-in-depth beyond FluentValidation)
        if (string.IsNullOrWhiteSpace(request.Path))
            return Result.Fail<LibraryRootDto>("Path is required.");

        if (request.Path.Length > 1024)
            return Result.Fail<LibraryRootDto>("Path must not exceed 1024 characters.");

        // Check that path starts with a configured NAS base path
        var configuredPaths = await nasService.GetConfiguredPathsAsync(cancellationToken);
        var isUnderConfiguredBase = configuredPaths.Any(base_ =>
            request.Path.StartsWith(base_, StringComparison.OrdinalIgnoreCase));

        if (!isUnderConfiguredBase)
            return Result.Fail<LibraryRootDto>(
                $"Path '{request.Path}' does not start with any configured NAS base path.");

        // Check for duplicate path
        var exists = await db.LibraryRoots
            .AnyAsync(r => r.Path == request.Path, cancellationToken);

        if (exists)
            return Result.Fail<LibraryRootDto>("LIBRARY_ROOT_DUPLICATE: A library root with this path already exists.");

        var root = new LibraryRoot
        {
            Path = request.Path,
            Kind = request.Kind,
            Label = request.Label,
            IsEnabled = true
        };

        db.LibraryRoots.Add(root);
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

