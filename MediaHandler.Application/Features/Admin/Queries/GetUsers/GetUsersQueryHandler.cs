using AutoMapper;
using FluentValidation;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Auth.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Admin.Queries.GetUsers;

public record GetUsersQuery(int Page = 1, int PageSize = 20, string? Search = null,
    string? SortField = null, string? SortOrder = "asc")
    : IRequest<Result<PagedResult<UserDto>>>;

public class GetUsersQueryValidator : AbstractValidator<GetUsersQuery>
{
    public GetUsersQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public class GetUsersQueryHandler(IApplicationDbContext context, IMapper mapper)
    : IRequestHandler<GetUsersQuery, Result<PagedResult<UserDto>>>
{
    public async Task<Result<PagedResult<UserDto>>> Handle(GetUsersQuery request, CancellationToken cancellationToken)
    {
        var query = context.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(u => u.Email.Contains(request.Search) ||
                                     (u.DisplayName != null && u.DisplayName.Contains(request.Search)));

        var total = await query.CountAsync(cancellationToken);

        var ordered = (request.SortField?.ToLowerInvariant(), request.SortOrder?.ToLowerInvariant() == "desc") switch
        {
            ("displayname", false) => query.OrderBy(u => u.DisplayName),
            ("displayname", true) => query.OrderByDescending(u => u.DisplayName),
            ("email", false) => query.OrderBy(u => u.Email),
            ("email", true) => query.OrderByDescending(u => u.Email),
            ("role", false) => query.OrderBy(u => u.Role),
            ("role", true) => query.OrderByDescending(u => u.Role),
            ("isactive", false) => query.OrderBy(u => u.IsActive),
            ("isactive", true) => query.OrderByDescending(u => u.IsActive),
            _ => query.OrderBy(u => u.Email),
        };

        var entities = await ordered
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var items = mapper.Map<List<UserDto>>(entities);

        return Result.Success(new PagedResult<UserDto>(items, total, request.Page, request.PageSize));
    }
}