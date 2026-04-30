#nullable enable

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
    bool EnabledOnly = false) : IRequest<Result<PagedResult<LibraryRootDto>>>;

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

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(r => r.Path)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(r => new LibraryRootDto(
                r.Id, r.Path, r.Kind, r.Label, r.IsEnabled, r.CreatedAt, r.UpdatedAt))
            .ToListAsync(cancellationToken);

        return Result.Success(new PagedResult<LibraryRootDto>(items, totalCount, request.Page, request.PageSize));
    }
}

