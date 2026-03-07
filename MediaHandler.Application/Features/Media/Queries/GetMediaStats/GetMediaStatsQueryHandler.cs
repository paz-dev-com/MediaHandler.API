using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Media.DTOs;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Media.Queries.GetMediaStats;

public record GetMediaStatsQuery() : IRequest<Result<MediaStatsDto>>;

public class GetMediaStatsQueryHandler(IApplicationDbContext context, ICurrentUserService currentUser)
    : IRequestHandler<GetMediaStatsQuery, Result<MediaStatsDto>>
{
    public async Task<Result<MediaStatsDto>> Handle(GetMediaStatsQuery request, CancellationToken cancellationToken)
    {
        var totalMedia = await context.Medias.CountAsync(cancellationToken);
        var films = await context.Medias.CountAsync(m => m.Type == MediaType.Film, cancellationToken);
        var tvShows = totalMedia - films;

        var totalFiles = await context.MediaFiles.CountAsync(cancellationToken);
        var unlinkedFiles = await context.MediaFiles.CountAsync(f => f.MediaId == null, cancellationToken);

        var userId = currentUser.OktaId is not null
            ? await context.Users
                .Where(u => u.OktaId == currentUser.OktaId)
                .Select(u => (Guid?)u.Id)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var watchedByUser = 0;
        if (userId.HasValue)
        {
            watchedByUser = await context.UserMedias
                .CountAsync(um => um.UserId == userId.Value && um.IsWatched, cancellationToken);
        }

        return Result.Success(new MediaStatsDto(
            totalMedia,
            films,
            tvShows,
            watchedByUser,
            totalMedia - watchedByUser,
            totalFiles,
            unlinkedFiles));
    }
}
