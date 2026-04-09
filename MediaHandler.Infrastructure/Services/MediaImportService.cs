using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaHandler.Infrastructure.Services;

/// <summary>
/// Infrastructure implementation of <see cref="IMediaImportService"/>.
/// Checks for an existing <c>Media</c> record by <c>TmdbId</c> before fetching
/// metadata from the TMDB API and persisting a new entity with its genres.
/// </summary>
public sealed class MediaImportService(
    IApplicationDbContext context,
    ITmdbService tmdb,
    ILogger<MediaImportService> logger)
    : IMediaImportService
{
    /// <inheritdoc/>
    public async Task<Result<Guid>> ImportOrGetExistingAsync(
        int tmdbId,
        string mediaType,
        string? language,
        CancellationToken ct)
    {
        var existing = await context.Medias
            .FirstOrDefaultAsync(m => m.TmdbId == tmdbId, ct);

        if (existing is not null)
        {
            logger.LogInformation(
                "Media with TmdbId {TmdbId} already exists (Id={MediaId}). Skipping import.",
                tmdbId, existing.Id);
            return Result.Success(existing.Id);
        }

        var resolvedLanguage = language ?? "en";

        logger.LogInformation(
            "Fetching TMDB details for TmdbId={TmdbId}, MediaType={MediaType}, Language={Language}.",
            tmdbId, mediaType, resolvedLanguage);

        var details = await tmdb.GetMediaDetailsAsync(tmdbId, mediaType, resolvedLanguage, ct);

        if (details is null)
        {
            logger.LogWarning(
                "TMDB returned no details for TmdbId={TmdbId} (MediaType={MediaType}).",
                tmdbId, mediaType);
            return Result.Fail<Guid>("Media not found on TMDB.");
        }

        var type = mediaType.Equals("tv", StringComparison.OrdinalIgnoreCase)
            ? MediaType.TvShow
            : MediaType.Film;

        var media = new Domain.Entities.Media
        {
            TmdbId = details.Id,
            Title = details.Title,
            OriginalTitle = details.OriginalTitle,
            Overview = details.Overview,
            Type = type,
            ReleaseDate = details.ReleaseDate,
            Runtime = details.Runtime,
            PosterPath = details.PosterPath,
            BackdropPath = details.BackdropPath,
            VoteAverage = details.VoteAverage,
            VoteCount = details.VoteCount,
            Language = details.Language,
            Genres = details.Genres?
                .Select(name => new Domain.Entities.MediaGenre { Name = name })
                .ToList() ?? []
        };

        context.Medias.Add(media);
        await context.SaveChangesAsync(ct);

        logger.LogInformation(
            "Media '{Title}' (TmdbId={TmdbId}) imported successfully with Id={MediaId}.",
            media.Title, media.TmdbId, media.Id);

        return Result.Success(media.Id);
    }
}

