using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Exceptions;
using MediaHandler.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Wishlist.Commands;

public record AddToWishlistCommand(int TmdbId, string Title, string? PosterPath, DateTime? ReleaseDate, string? Notes) : IRequest<Result<Guid>>;

public class AddToWishlistCommandHandler : IRequestHandler<AddToWishlistCommand, Result<Guid>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public AddToWishlistCommandHandler(IApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(AddToWishlistCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId ?? throw new UnauthorizedAccessException();

        var existing = await _context.WishlistItems
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

        _context.WishlistItems.Add(item);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(item.Id);
    }
}

public record RemoveFromWishlistCommand(Guid Id) : IRequest<Result>;

public class RemoveFromWishlistCommandHandler : IRequestHandler<RemoveFromWishlistCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public RemoveFromWishlistCommandHandler(IApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(RemoveFromWishlistCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId ?? throw new UnauthorizedAccessException();

        var item = await _context.WishlistItems
            .FirstOrDefaultAsync(w => w.Id == request.Id && w.UserId == userId, cancellationToken)
            ?? throw new NotFoundException(nameof(WishlistItem), request.Id);

        _context.WishlistItems.Remove(item);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
