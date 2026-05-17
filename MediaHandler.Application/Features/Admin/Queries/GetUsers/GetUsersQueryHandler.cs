using AutoMapper;
using FluentValidation;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Auth.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Admin.Queries.GetUsers;

public record GetUsersQuery(int Page = 1, int PageSize = 20, string? Search = null)
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
        var entities = await query
            .OrderBy(u => u.Email)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var items = mapper.Map<List<UserDto>>(entities);

        return Result.Success(new PagedResult<UserDto>(items, total, request.Page, request.PageSize));
    }
}