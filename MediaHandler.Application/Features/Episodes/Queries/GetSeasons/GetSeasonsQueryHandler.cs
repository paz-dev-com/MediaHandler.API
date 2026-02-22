using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Episodes.DTOs;
using MediaHandler.Domain.Exceptions;
using MediaHandler.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Episodes.Queries.GetSeasons;

public record GetSeasonsQuery(Guid MediaId) : IRequest<Result<IReadOnlyList<TvSeasonDto>>>;

public class GetSeasonsQueryHandler : IRequestHandler<GetSeasonsQuery, Result<IReadOnlyList<TvSeasonDto>>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public GetSeasonsQueryHandler(IApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<TvSeasonDto>>> Handle(GetSeasonsQuery request, CancellationToken cancellationToken)
    {
        var mediaExists = await _context.Medias.AnyAsync(m => m.Id == request.MediaId, cancellationToken);
        if (!mediaExists)
            throw new NotFoundException(nameof(Domain.Entities.Media), request.MediaId);

        var userId = _currentUser.UserId;

        var seasons = await _context.TvSeasons
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
