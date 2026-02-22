using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Domain.Enums;
using MediaHandler.Domain.Exceptions;
using MediaHandler.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Tmdb.Commands.ImportFromTmdb;

public record ImportFromTmdbCommand(int TmdbId, string MediaType, string? Language = null) : IRequest<Result<Guid>>;

public class ImportFromTmdbCommandHandler : IRequestHandler<ImportFromTmdbCommand, Result<Guid>>
{
    private readonly IApplicationDbContext _context;
    private readonly ITmdbService _tmdb;
    private readonly ICurrentUserService _currentUser;

    public ImportFromTmdbCommandHandler(IApplicationDbContext context, ITmdbService tmdb, ICurrentUserService currentUser)
    {
        _context = context;
        _tmdb = tmdb;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(ImportFromTmdbCommand request, CancellationToken cancellationToken)
    {
        var existing = await _context.Medias
            .FirstOrDefaultAsync(m => m.TmdbId == request.TmdbId, cancellationToken);

        if (existing is not null)
            return Result.Success(existing.Id);

        var language = request.Language ?? "en";
        var details = await _tmdb.GetMediaDetailsAsync(request.TmdbId, request.MediaType, language, cancellationToken)
            ?? throw new NotFoundException("TMDB media", request.TmdbId);

        var mediaType = request.MediaType.Equals("tv", StringComparison.OrdinalIgnoreCase)
            ? MediaType.TvShow : MediaType.Film;

        var media = new Domain.Entities.Media
        {
            TmdbId = details.Id,
            Title = details.Title,
            OriginalTitle = details.OriginalTitle,
            Overview = details.Overview,
            Type = mediaType,
            ReleaseDate = details.ReleaseDate,
            Runtime = details.Runtime,
            PosterPath = details.PosterPath,
            BackdropPath = details.BackdropPath,
            VoteAverage = details.VoteAverage,
            VoteCount = details.VoteCount,
            Genres = details.Genres,
            Language = details.Language
        };

        _context.Medias.Add(media);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(media.Id);
    }
}
