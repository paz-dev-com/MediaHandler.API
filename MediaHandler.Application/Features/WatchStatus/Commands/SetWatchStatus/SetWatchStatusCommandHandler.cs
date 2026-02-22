using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Exceptions;
using MediaHandler.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.WatchStatus.Commands.SetWatchStatus;

public record SetWatchStatusCommand(Guid MediaId, bool IsWatched) : IRequest<Result>;

public class SetWatchStatusCommandHandler : IRequestHandler<SetWatchStatusCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public SetWatchStatusCommandHandler(IApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(SetWatchStatusCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId
            ?? throw new UnauthorizedAccessException();

        var mediaExists = await _context.Medias.AnyAsync(m => m.Id == request.MediaId, cancellationToken);
        if (!mediaExists)
            throw new NotFoundException(nameof(Domain.Entities.Media), request.MediaId);

        var userMedia = await _context.UserMedias
            .FirstOrDefaultAsync(um => um.UserId == userId && um.MediaId == request.MediaId, cancellationToken);

        if (userMedia is null)
        {
            userMedia = new UserMedia { UserId = userId, MediaId = request.MediaId };
            _context.UserMedias.Add(userMedia);
        }

        userMedia.IsWatched = request.IsWatched;
        userMedia.WatchedAt = request.IsWatched ? DateTime.UtcNow : null;
        userMedia.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
