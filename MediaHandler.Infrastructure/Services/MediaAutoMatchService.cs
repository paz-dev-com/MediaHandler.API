using MediaHandler.Application.Common.DTOs;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace MediaHandler.Infrastructure.Services;

/// <summary>
/// Infrastructure implementation of <see cref="IMediaAutoMatchService"/>.
/// Iterates over unlinked <see cref="MediaFile"/> records, parses each filename,
/// queries TMDB, imports (or retrieves) the matching <c>Media</c> entity, and sets
/// <c>MediaFile.MediaId</c>. Changes are flushed to the database in batches of 10.
/// A 250 ms delay is introduced between TMDB API calls to respect the rate limit.
/// </summary>
public sealed class MediaAutoMatchService(
    IMediaFileNameParser parser,
    ITmdbService tmdb,
    IMediaImportService importer,
    IApplicationDbContext context,
    ILogger<MediaAutoMatchService> logger)
    : IMediaAutoMatchService
{
    private const int BatchSize = 10;
    private const int TmdbDelayMs = 250;

    /// <inheritdoc/>
    public async Task<AutoMatchResult> MatchAndLinkUnlinkedFilesAsync(
        IReadOnlyList<MediaFile> unlinkedFiles,
        string language,
        CancellationToken ct)
    {
        int matched = 0, skipped = 0, failed = 0;
        var errors = new List<string>();
        var processedSinceSave = 0;

        logger.LogInformation(
            "Starting auto-match for {Count} unlinked MediaFile(s).", unlinkedFiles.Count);

        for (var i = 0; i < unlinkedFiles.Count; i++)
        {
            var file = unlinkedFiles[i];

            try
            {
                // 1. Parse filename
                var parsed = parser.Parse(file.FilePath);
                if (parsed is null)
                {
                    logger.LogWarning(
                        "Could not parse filename for MediaFile {FileId} ('{FilePath}'). Skipping.",
                        file.Id, file.FilePath);
                    skipped++;
                    continue;
                }

                // 2. Search TMDB — append year when available for accuracy, fall back without it
                var query = parsed.Year.HasValue
                    ? $"{parsed.Title} {parsed.Year}"
                    : parsed.Title;

                var searchResult = await tmdb.SearchMediaAsync(query, language, ct);

                if (searchResult is null && parsed.Year.HasValue)
                {
                    logger.LogInformation(
                        "TMDB search with year found nothing for '{Query}'. Retrying without year.",
                        query);
                    searchResult = await tmdb.SearchMediaAsync(parsed.Title, language, ct);
                    await Task.Delay(TmdbDelayMs, ct);
                }

                if (searchResult is null)
                {
                    logger.LogInformation(
                        "No TMDB result for '{Title}' (MediaFile {FileId}). Skipping.",
                        parsed.Title, file.Id);
                    skipped++;
                    processedSinceSave++;
                    continue;
                }

                // 3. Determine media type — prefer hint from parser, fall back to TMDB result
                var mediaType = parsed.MediaTypeHint ?? searchResult.MediaType;

                // 4. Import or retrieve existing Media entity
                var importResult = await importer.ImportOrGetExistingAsync(
                    searchResult.Id, mediaType, language, ct);

                if (!importResult.IsSuccess)
                {
                    logger.LogWarning(
                        "Import failed for MediaFile {FileId} (TmdbId={TmdbId}): {Error}",
                        file.Id, searchResult.Id, string.Join("; ", importResult.Errors));
                    errors.Add($"[{file.FilePath}] {string.Join("; ", importResult.Errors)}");
                    failed++;
                    processedSinceSave++;
                    continue;
                }

                // 5. Link MediaFile → Media
                file.MediaId = importResult.Value;
                matched++;
                processedSinceSave++;

                logger.LogInformation(
                    "Linked MediaFile {FileId} → Media {MediaId} ('{Title}').",
                    file.Id, importResult.Value, searchResult.Title);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex,
                    "Unexpected error while processing MediaFile {FileId} ('{FilePath}').",
                    file.Id, file.FilePath);
                errors.Add($"[{file.FilePath}] {ex.Message}");
                failed++;
                processedSinceSave++;
            }

            // Batch save every BatchSize files
            if (processedSinceSave >= BatchSize)
            {
                await context.SaveChangesAsync(ct);
                processedSinceSave = 0;
            }

            // Respect TMDB rate limit between calls
            if (i < unlinkedFiles.Count - 1)
                await Task.Delay(TmdbDelayMs, ct);
        }

        // Flush remaining changes
        if (processedSinceSave > 0)
            await context.SaveChangesAsync(ct);

        logger.LogInformation(
            "Auto-match complete. Matched={Matched}, Skipped={Skipped}, Failed={Failed}.",
            matched, skipped, failed);

        return new AutoMatchResult(matched, skipped, failed, errors);
    }
}


