using AutoMapper;
using MediaHandler.Application.Common.Extensions;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Wishlist.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Wishlist.Commands.MarkWishlistAcquired;

public record MarkWishlistAcquiredCommand(Guid Id, bool IsAcquired) : IRequest<Result<WishlistItemDto>>;

public class MarkWishlistAcquiredCommandHandler(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    IMapper mapper)
    : IRequestHandler<MarkWishlistAcquiredCommand, Result<WishlistItemDto>>
{
    public async Task<Result<WishlistItemDto>> Handle(MarkWishlistAcquiredCommand request,
        CancellationToken cancellationToken)
    {
        var userId = await currentUser.ResolveUserIdAsync(context, cancellationToken);

        var item = await context.WishlistItems
            .FirstOrDefaultAsync(w => w.Id == request.Id && w.UserId == userId, cancellationToken);

        if (item is null)
            return Result.Fail<WishlistItemDto>("Wishlist item not found.");

        item.IsAcquired = request.IsAcquired;
        item.AcquiredAt = request.IsAcquired ? DateTime.UtcNow : null;
        item.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(mapper.Map<WishlistItemDto>(item));
    }
}