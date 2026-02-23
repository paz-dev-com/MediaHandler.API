using MediaHandler.Application.Common.DTOs;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Tmdb.Commands.ImportFromTmdb;

public record ImportFromTmdbCommand(int TmdbId, string MediaType, string? Language = null) : IRequest<Result<Guid>>;

public class ImportFromTmdbCommandHandler(IApplicationDbContext context, ITmdbService tmdb, ICurrentUserService currentUser)
    : IRequestHandler<ImportFromTmdbCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(ImportFromTmdbCommand request, CancellationToken cancellationToken)
    {
        var existing = await context.Medias
            .FirstOrDefaultAsync(m => m.TmdbId == request.TmdbId, cancellationToken);

        if (existing is not null)
            return Result.Success(existing.Id);

        var language = request.Language ?? "en";

        var details = await tmdb.GetMediaDetailsAsync(request.TmdbId, request.MediaType, language, cancellationToken);

        if (details is null)
            return Result.Fail<Guid>("Media not found on TMDB.");

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
            Language = details.Language,
            Genres = details.Genres?.Select(name => new Domain.Entities.MediaGenre { Name = name }).ToList() ?? []
        };

        context.Medias.Add(media);
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(media.Id);
    }
}
