using FluentValidation;
using MediaHandler.Application.Common.DTOs;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.LibraryRoots.Queries.ListLibraryRoots;

public record ListLibraryRootsQuery(
    int Page = 1,
    int PageSize = 20,
    LibraryRootKind? Kind = null,
    bool EnabledOnly = false,
    string? SortField = null,
    string? SortOrder = "asc",
    string? Path = null) : IRequest<Result<PagedResult<LibraryRootDto>>>;

public class ListLibraryRootsQueryValidator : AbstractValidator<ListLibraryRootsQuery>
{
    public ListLibraryRootsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class ListLibraryRootsQueryHandler(IApplicationDbContext db)
    : IRequestHandler<ListLibraryRootsQuery, Result<PagedResult<LibraryRootDto>>>
{
    public async Task<Result<PagedResult<LibraryRootDto>>> Handle(
        ListLibraryRootsQuery request,
        CancellationToken cancellationToken)
    {
        var query = db.LibraryRoots.AsNoTracking();

        if (request.Kind.HasValue)
            query = query.Where(r => r.Kind == request.Kind.Value);

        if (request.EnabledOnly)
            query = query.Where(r => r.IsEnabled);

        if (!string.IsNullOrWhiteSpace(request.Path))
            query = query.Where(r => r.Path.Contains(request.Path));

        var totalCount = await query.CountAsync(cancellationToken);

        var ordered = (request.SortField?.ToLowerInvariant(), request.SortOrder?.ToLowerInvariant() == "desc") switch
        {
            ("path", false) => query.OrderBy(r => r.Path),
            ("path", true) => query.OrderByDescending(r => r.Path),
            ("createdat", false) => query.OrderBy(r => r.CreatedAt),
            ("createdat", true) => query.OrderByDescending(r => r.CreatedAt),
            _ => query.OrderBy(r => r.Path),
        };

        var items = await ordered
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(r => new LibraryRootDto(
                r.Id, r.Path, r.Kind, r.Label, r.IsEnabled, r.CreatedAt, r.UpdatedAt))
            .ToListAsync(cancellationToken);

        return Result.Success(new PagedResult<LibraryRootDto>(items, totalCount, request.Page, request.PageSize));
    }
}