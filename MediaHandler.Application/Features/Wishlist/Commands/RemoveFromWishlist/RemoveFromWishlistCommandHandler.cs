using MediaHandler.Application.Common.Extensions;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Wishlist.Commands.RemoveFromWishlist;

public record RemoveFromWishlistCommand(Guid Id) : IRequest<Result>;

public class RemoveFromWishlistCommandHandler(IApplicationDbContext context, ICurrentUserService currentUser)
    : IRequestHandler<RemoveFromWishlistCommand, Result>
{
    public async Task<Result> Handle(RemoveFromWishlistCommand request, CancellationToken cancellationToken)
    {
        var userId = await currentUser.ResolveUserIdAsync(context, cancellationToken);

        var item = await context.WishlistItems
            .FirstOrDefaultAsync(w => w.Id == request.Id && w.UserId == userId, cancellationToken);

        if (item is null)
            return Result.Fail("Wishlist item not found.");

        context.WishlistItems.Remove(item);
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
