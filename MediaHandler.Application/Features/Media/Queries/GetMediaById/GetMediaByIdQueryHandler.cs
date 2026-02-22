using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Media.DTOs;
using MediaHandler.Domain.Exceptions;
using MediaHandler.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Media.Queries.GetMediaById;

public record GetMediaByIdQuery(Guid Id) : IRequest<Result<MediaDto>>;

public class GetMediaByIdQueryHandler : IRequestHandler<GetMediaByIdQuery, Result<MediaDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public GetMediaByIdQueryHandler(IApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<MediaDto>> Handle(GetMediaByIdQuery request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;

        var media = await _context.Medias
            .AsNoTracking()
            .Include(m => m.MediaFiles)
            .Include(m => m.UserMedias.Where(um => userId.HasValue && um.UserId == userId.Value))
            .FirstOrDefaultAsync(m => m.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Domain.Entities.Media), request.Id);

        var userMedia = media.UserMedias.FirstOrDefault();

        return Result.Success(new MediaDto(
            media.Id, media.TmdbId, media.Title, media.OriginalTitle, media.Overview,
            media.Type, media.ReleaseDate, media.Runtime, media.PosterPath, media.BackdropPath,
            media.VoteAverage, media.Genres, media.MediaFiles.Count,
            userMedia?.IsWatched));
    }
}
