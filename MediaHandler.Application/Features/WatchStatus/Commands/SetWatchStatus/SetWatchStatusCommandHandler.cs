using MediaHandler.Application.Common.Extensions;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.WatchStatus.Commands.SetWatchStatus;

public record SetWatchStatusCommand(Guid MediaId, bool IsWatched) : IRequest<Result>;

public class SetWatchStatusCommandHandler(IApplicationDbContext context, ICurrentUserService currentUser)
    : IRequestHandler<SetWatchStatusCommand, Result>
{
    public async Task<Result> Handle(SetWatchStatusCommand request, CancellationToken cancellationToken)
    {
        var userId = await currentUser.ResolveUserIdAsync(context, cancellationToken);

        var mediaExists = await context.Medias.AnyAsync(m => m.Id == request.MediaId, cancellationToken);
        if (!mediaExists)
            return Result.Fail("Media not found.");

        var userMedia = await context.UserMedias
            .FirstOrDefaultAsync(um => um.UserId == userId && um.MediaId == request.MediaId, cancellationToken);

        if (userMedia is null)
        {
            userMedia = new UserMedia { UserId = userId, MediaId = request.MediaId };
            context.UserMedias.Add(userMedia);
        }

        userMedia.IsWatched = request.IsWatched;
        userMedia.WatchedAt = request.IsWatched ? DateTime.UtcNow : null;
        userMedia.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}