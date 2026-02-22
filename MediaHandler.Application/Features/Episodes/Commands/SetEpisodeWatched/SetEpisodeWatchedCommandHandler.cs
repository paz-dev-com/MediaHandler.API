using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Exceptions;
using MediaHandler.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Episodes.Commands.SetEpisodeWatched;

public record SetEpisodeWatchedCommand(Guid EpisodeId, bool IsWatched) : IRequest<Result>;

public class SetEpisodeWatchedCommandHandler : IRequestHandler<SetEpisodeWatchedCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public SetEpisodeWatchedCommandHandler(IApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(SetEpisodeWatchedCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId ?? throw new UnauthorizedAccessException();

        var episodeExists = await _context.TvEpisodes.AnyAsync(e => e.Id == request.EpisodeId, cancellationToken);
        if (!episodeExists)
            throw new NotFoundException(nameof(TvEpisode), request.EpisodeId);

        var userEpisode = await _context.UserEpisodes
            .FirstOrDefaultAsync(ue => ue.UserId == userId && ue.EpisodeId == request.EpisodeId, cancellationToken);

        if (userEpisode is null)
        {
            userEpisode = new UserEpisode { UserId = userId, EpisodeId = request.EpisodeId };
            _context.UserEpisodes.Add(userEpisode);
        }

        userEpisode.IsWatched = request.IsWatched;
        userEpisode.WatchedAt = request.IsWatched ? DateTime.UtcNow : null;
        userEpisode.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
