using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Episodes.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Episodes.Queries.GetSeasons;

public record GetSeasonsQuery(Guid MediaId) : IRequest<Result<IReadOnlyList<TvSeasonDto>>>;

public class GetSeasonsQueryHandler(IApplicationDbContext context, ICurrentUserService currentUser)
    : IRequestHandler<GetSeasonsQuery, Result<IReadOnlyList<TvSeasonDto>>>
{
    public async Task<Result<IReadOnlyList<TvSeasonDto>>> Handle(GetSeasonsQuery request, CancellationToken cancellationToken)
    {
        var mediaExists = await context.Medias.AnyAsync(m => m.Id == request.MediaId, cancellationToken);
        if (!mediaExists)
            return Result.Fail<IReadOnlyList<TvSeasonDto>>("Media not found.");

        var oktaId = currentUser.OktaId;
        Guid? userId = null;
        if (oktaId is not null)
        {
            userId = await context.Users
                .AsNoTracking()
                .Where(u => u.OktaId == oktaId)
                .Select(u => (Guid?)u.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var seasons = await context.TvSeasons
            .AsNoTracking()
            .Where(s => s.MediaId == request.MediaId)
            .Include(s => s.TvEpisodes)
                .ThenInclude(e => e.UserEpisodes.Where(ue => userId.HasValue && ue.UserId == userId.Value))
            .OrderBy(s => s.SeasonNumber)
            .ToListAsync(cancellationToken);

        var result = seasons.Select(s => new TvSeasonDto(
            s.Id, s.SeasonNumber, s.Name, s.Overview, s.AirDate, s.PosterPath,
            s.TvEpisodes.Count,
            s.TvEpisodes.Count(e => e.UserEpisodes.Any(ue => ue.IsWatched)),
            s.TvEpisodes.OrderBy(e => e.EpisodeNumber).Select(e => new TvEpisodeDto(
                e.Id, e.EpisodeNumber, e.Name, e.Overview, e.AirDate, e.StillPath, e.Runtime,
                e.UserEpisodes.Any(ue => ue.IsWatched)
            )).ToList()
        )).ToList();

        return Result.Success<IReadOnlyList<TvSeasonDto>>(result);
    }
}
