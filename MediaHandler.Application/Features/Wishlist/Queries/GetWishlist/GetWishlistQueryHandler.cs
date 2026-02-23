using AutoMapper;
using AutoMapper.QueryableExtensions;
using FluentValidation;
using MediaHandler.Application.Common.Extensions;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Wishlist.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Wishlist.Queries.GetWishlist;

public record GetWishlistQuery(int Page = 1, int PageSize = 20) : IRequest<Result<PagedResult<WishlistItemDto>>>;

public class GetWishlistQueryValidator : AbstractValidator<GetWishlistQuery>
{
    public GetWishlistQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public class GetWishlistQueryHandler(IApplicationDbContext context, ICurrentUserService currentUser, IMapper mapper)
    : IRequestHandler<GetWishlistQuery, Result<PagedResult<WishlistItemDto>>>
{
    public async Task<Result<PagedResult<WishlistItemDto>>> Handle(GetWishlistQuery request, CancellationToken cancellationToken)
    {
        var userId = await currentUser.ResolveUserIdAsync(context, cancellationToken);

        var query = context.WishlistItems.AsNoTracking().Where(w => w.UserId == userId);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(w => w.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ProjectTo<WishlistItemDto>(mapper.ConfigurationProvider)
            .ToListAsync(cancellationToken);

        return Result.Success(new PagedResult<WishlistItemDto>(items, total, request.Page, request.PageSize));
    }
}
