using MediaHandler.Application.Common.Extensions;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Episodes.Commands.SetEpisodeWatched;

public record SetEpisodeWatchedCommand(Guid EpisodeId, bool IsWatched) : IRequest<Result>;

public class SetEpisodeWatchedCommandHandler(IApplicationDbContext context, ICurrentUserService currentUser)
    : IRequestHandler<SetEpisodeWatchedCommand, Result>
{
    public async Task<Result> Handle(SetEpisodeWatchedCommand request, CancellationToken cancellationToken)
    {
        var userId = await currentUser.ResolveUserIdAsync(context, cancellationToken);

        var episodeExists = await context.TvEpisodes.AnyAsync(e => e.Id == request.EpisodeId, cancellationToken);
        if (!episodeExists)
            return Result.Fail("Episode not found.");

        var userEpisode = await context.UserEpisodes
            .FirstOrDefaultAsync(ue => ue.UserId == userId && ue.EpisodeId == request.EpisodeId, cancellationToken);

        if (userEpisode is null)
        {
            userEpisode = new UserEpisode { UserId = userId, EpisodeId = request.EpisodeId };
            context.UserEpisodes.Add(userEpisode);
        }

        userEpisode.IsWatched = request.IsWatched;
        userEpisode.WatchedAt = request.IsWatched ? DateTime.UtcNow : null;
        userEpisode.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}