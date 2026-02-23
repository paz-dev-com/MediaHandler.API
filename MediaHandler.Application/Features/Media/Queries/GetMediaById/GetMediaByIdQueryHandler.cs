using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Media.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Media.Queries.GetMediaById;

public record GetMediaByIdQuery(Guid Id) : IRequest<Result<MediaDto>>;

public class GetMediaByIdQueryHandler(IApplicationDbContext context, ICurrentUserService currentUser)
    : IRequestHandler<GetMediaByIdQuery, Result<MediaDto>>
{
    public async Task<Result<MediaDto>> Handle(GetMediaByIdQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;

        var media = await context.Medias
            .AsNoTracking()
            .Include(m => m.MediaFiles)
            .Include(m => m.Genres)
            .Include(m => m.UserMedias.Where(um => userId.HasValue && um.UserId == userId.Value))
            .FirstOrDefaultAsync(m => m.Id == request.Id, cancellationToken);

        if (media is null)
            return Result.Fail<MediaDto>("Media not found.");

        var userMedia = media.UserMedias.FirstOrDefault();

        return Result.Success(new MediaDto(
            media.Id, media.TmdbId, media.Title, media.OriginalTitle, media.Overview,
            media.Type, media.ReleaseDate, media.Runtime, media.PosterPath, media.BackdropPath,
            media.VoteAverage,
            media.Genres.Select(g => g.Name).ToList().AsReadOnly(),
            media.MediaFiles.Count,
            userMedia?.IsWatched));
    }
}
