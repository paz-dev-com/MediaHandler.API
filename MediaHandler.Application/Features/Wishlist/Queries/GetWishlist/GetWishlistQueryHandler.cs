using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Wishlist.DTOs;
using MediaHandler.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Wishlist.Queries.GetWishlist;

public record GetWishlistQuery(int Page = 1, int PageSize = 20) : IRequest<Result<PagedResult<WishlistItemDto>>>;

public class GetWishlistQueryHandler : IRequestHandler<GetWishlistQuery, Result<PagedResult<WishlistItemDto>>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public GetWishlistQueryHandler(IApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<PagedResult<WishlistItemDto>>> Handle(GetWishlistQuery request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId ?? throw new UnauthorizedAccessException();

        var query = _context.WishlistItems.AsNoTracking().Where(w => w.UserId == userId);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(w => w.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(w => new WishlistItemDto(w.Id, w.TmdbId, w.Title, w.PosterPath,
                w.ReleaseDate, w.IsAcquired, w.AcquiredAt, w.Notes, w.CreatedAt))
            .ToListAsync(cancellationToken);

        return Result.Success(new PagedResult<WishlistItemDto>(items, total, request.Page, request.PageSize));
    }
}
