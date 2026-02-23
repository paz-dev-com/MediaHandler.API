using MediaHandler.Application.Common.Extensions;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Wishlist.Commands.AddToWishlist;

public record AddToWishlistCommand(int TmdbId, string Title, string? PosterPath, DateTime? ReleaseDate, string? Notes) : IRequest<Result<Guid>>;

public class AddToWishlistCommandHandler(IApplicationDbContext context, ICurrentUserService currentUser)
    : IRequestHandler<AddToWishlistCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(AddToWishlistCommand request, CancellationToken cancellationToken)
    {
        var userId = await currentUser.ResolveUserIdAsync(context, cancellationToken);

        var existing = await context.WishlistItems
            .AnyAsync(w => w.UserId == userId && w.TmdbId == request.TmdbId, cancellationToken);

        if (existing)
            return Result.Fail<Guid>("This title is already in your wishlist.");

        var item = new WishlistItem
        {
            UserId = userId,
            TmdbId = request.TmdbId,
            Title = request.Title,
            PosterPath = request.PosterPath,
            ReleaseDate = request.ReleaseDate,
            Notes = request.Notes
        };

        context.WishlistItems.Add(item);
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(item.Id);
    }
}
