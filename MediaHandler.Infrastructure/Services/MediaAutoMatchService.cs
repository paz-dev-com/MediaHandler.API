using MediaHandler.Application.Common;
using MediaHandler.Application.Common.DTOs;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace MediaHandler.Infrastructure.Services;

/// <summary>
/// Infrastructure implementation of <see cref="IMediaAutoMatchService"/>.
/// <para>
/// Iterates over unlinked <see cref="MediaFile"/> records, parses each filename,
/// queries TMDB, imports (or retrieves) the matching <c>Media</c> entity, and sets
/// <c>MediaFile.MediaId</c>. Changes are flushed to the database in batches of 10.
/// </para>
/// <para>
/// TMDB rate limit: 40 requests per 10 seconds (4 req/s). A <see cref="TmdbDelayMs"/>
/// delay is applied after every TMDB HTTP call to stay within this limit.
/// An in-memory title cache prevents duplicate TMDB round-trips for TV shows
/// that have many episode files sharing the same title (e.g., 20 episodes of one
/// series only trigger 1 TMDB search + 1 details call instead of 20).
/// The Polly <c>StandardResilienceHandler</c> on the TMDB HttpClient handles any
/// HTTP 429 responses automatically with exponential back-off.
/// </para>
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

    /// <summary>
    /// Delay in milliseconds applied after every TMDB API call.
    /// TMDB allows 40 req/10 s (~4 req/s → 250 ms minimum spacing).
    /// Using 300 ms gives a comfortable margin when two calls are made
    /// per file (search + details).
    /// </summary>
    private const int TmdbDelayMs = 300;

    /// <inheritdoc/>
    public async Task<AutoMatchResult> MatchAndLinkUnlinkedFilesAsync(
        IReadOnlyList<MediaFile> unlinkedFiles,
        string language,
        CancellationToken ct)
    {
        int matched = 0, skipped = 0, failed = 0;
        var errors = new List<string>();
        var processedSinceSave = 0;

        // In-memory caches for the current batch.
        // Avoids duplicate TMDB round-trips when multiple files share the same
        // show/movie title (very common for TV series with many episodes).
        var resolvedCache = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var noResultCache = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        logger.LogInformation(
            "Starting auto-match for {Count} unlinked MediaFile(s).", unlinkedFiles.Count);

        foreach (var file in unlinkedFiles)
        {
            try
            {
                // 1. Parse filename
                var parsed = parser.Parse(file.FilePath);
                if (parsed is null)
                {
                    // Non-video files that slipped into the MediaFiles table are silently
                    // skipped — they are not failures.
                    if (!MediaFileConstants.IsVideoFile(file.FilePath))
                    {
                        logger.LogDebug(
                            "Skipping non-video MediaFile {FileId} ('{FilePath}').",
                            file.Id, file.FilePath);
                        skipped++;
                        processedSinceSave++;
                        continue;
                    }

                    logger.LogWarning(
                        "Could not parse filename for MediaFile {FileId} ('{FilePath}'). Counting as failed.",
                        file.Id, file.FilePath);
                    errors.Add($"[{file.FilePath}] Unable to extract a usable title from the filename.");
                    failed++;
                    continue;
                }

                // Build a cache key: "Title|Year" (Year=0 when unknown)
                var cacheKey = $"{parsed.Title.ToLowerInvariant()}|{parsed.Year ?? 0}";

                // 2a. Cache hit — already matched earlier in this batch
                if (resolvedCache.TryGetValue(cacheKey, out var cachedMediaId))
                {
                    file.MediaId = cachedMediaId;
                    matched++;
                    processedSinceSave++;
                    logger.LogInformation(
                        "Linked MediaFile {FileId} → Media {MediaId} ('{Title}') [cache hit].",
                        file.Id, cachedMediaId, parsed.Title);
                    continue;
                }

                // 2b. Cache hit — already had no TMDB result earlier in this batch
                if (noResultCache.Contains(cacheKey))
                {
                    logger.LogDebug(
                        "Skipping MediaFile {FileId} ('{Title}'): no TMDB match found earlier in this batch.",
                        file.Id, parsed.Title);
                    skipped++;
                    processedSinceSave++;
                    continue;
                }

                // 3. Search TMDB — append year for accuracy, fall back without it
                var query = parsed.Year.HasValue
                    ? $"{parsed.Title} {parsed.Year}"
                    : parsed.Title;

                var searchResult = await tmdb.SearchMediaAsync(query, language, ct);
                await Task.Delay(TmdbDelayMs, ct); // respect rate limit after every call

                if (searchResult is null && parsed.Year.HasValue)
                {
                    logger.LogInformation(
                        "TMDB search with year found nothing for '{Query}'. Retrying without year.", query);
                    searchResult = await tmdb.SearchMediaAsync(parsed.Title, language, ct);
                    await Task.Delay(TmdbDelayMs, ct);
                }

                if (searchResult is null)
                {
                    logger.LogInformation(
                        "No TMDB result for '{Title}' (MediaFile {FileId}). Skipping.",
                        parsed.Title, file.Id);
                    noResultCache.Add(cacheKey);
                    skipped++;
                    processedSinceSave++;
                    continue;
                }

                // 4. Determine media type — prefer hint from parser, fall back to TMDB result
                var mediaType = parsed.MediaTypeHint ?? searchResult.MediaType;

                // 5. Import or retrieve existing Media entity
                //    ImportOrGetExistingAsync checks the DB first; only calls TMDB when the
                //    Media does not yet exist. The delay after the call accounts for that.
                var importResult = await importer.ImportOrGetExistingAsync(
                    searchResult.Id, mediaType, language, ct);
                await Task.Delay(TmdbDelayMs, ct); // delay covers the possible GetMediaDetails call

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

                // 6. Link MediaFile → Media and populate caches
                file.MediaId = importResult.Value;
                resolvedCache[cacheKey] = importResult.Value;
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


