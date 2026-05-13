// EnrichmentCoordinator — singleton service implementing IEnrichmentCoordinator.
// Owns the lifecycle of background TMDB batch enrichment runs.
// Follows the same pattern as ScanRunCoordinator.
//
// Scaffold + DI + state transitions (Pending → Running → Completed|Failed) + stale-run guard
// Incremental entry selection (Overview IS NULL OR UpdatedAt > lastEnrichmentFinishedAt)
// Media field population (Title, Overview, Runtime, PosterPath, Status, genres, etc.)
// TvSeason/TvEpisode upsert for TV shows
// Per-entry error tracking + progress reporting every 10 entries or 5 seconds
// T061: records per-media processing results in EnrichedMediaIdsJson

using System.Text.Json;
using MediaHandler.Application.Common.DTOs;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Features.Dashboard.DTOs;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Enums;
using MediaHandler.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediaHandler.Infrastructure.Services;

/// <summary>Lightweight record used to track per-media enrichment outcome in <c>EnrichedMediaIdsJson</c>.</summary>
/// <param name="MediaId">The <c>Media</c> row identifier.</param>
/// <param name="Status">Processing outcome: <c>Enriched</c>, <c>Failed</c>, or <c>Skipped</c>.</param>
internal record EnrichmentMediaResult(Guid MediaId, string Status);

/// <summary>
///     Singleton coordinator that owns the lifecycle of background batch TMDB enrichment runs.
///     <para>
///         Uses <see cref="IServiceScopeFactory" /> internally to resolve scoped dependencies
///         (DbContext, ITmdbService) per run — avoids captive-dependency issues.
///     </para>
///     <para>
///         At most one enrichment run may be active at a time; enforced at the DB level by a
///         filtered unique index on <c>EnrichmentRuns.Status = 'Running'</c> and at the application
///         level by <c>StartEnrichmentCommandHandler</c>.
///     </para>
/// </summary>
public sealed class EnrichmentCoordinator : IEnrichmentCoordinator
{
    private readonly ILogger<EnrichmentCoordinator> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public EnrichmentCoordinator(
        ILogger<EnrichmentCoordinator> logger,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    // =========================================================================
    // IEnrichmentCoordinator.StartAsync
    // =========================================================================

    /// <inheritdoc />
    public Task StartAsync(Guid enrichmentRunId, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "EnrichmentCoordinator: starting background enrichment run {RunId}.", enrichmentRunId);

        // Fire-and-forget; the background task owns its own DI scope.
        _ = Task.Run(() => ExecuteEnrichmentAsync(enrichmentRunId), CancellationToken.None);

        return Task.CompletedTask;
    }

    // =========================================================================
    // IEnrichmentCoordinator.GetStatusAsync
    // =========================================================================

    /// <inheritdoc />
    public async Task<EnrichmentRunDto?> GetStatusAsync(CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaHandlerDbContext>();

        var run = await db.EnrichmentRuns
            .AsNoTracking()
            .OrderByDescending(r => r.StartedAt)
            .FirstOrDefaultAsync(ct);

        return run is null ? null : MapToDto(run);
    }

    // =========================================================================
    // Background execution — owns a dedicated DI scope
    // =========================================================================

    private async Task ExecuteEnrichmentAsync(Guid runId)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaHandlerDbContext>();
        var tmdbService = scope.ServiceProvider.GetRequiredService<ITmdbService>();

        // Stale-run guard: confirm the row still exists in Pending state
        var run = await db.EnrichmentRuns.FirstOrDefaultAsync(r => r.Id == runId);
        if (run is null)
        {
            _logger.LogWarning(
                "EnrichmentCoordinator: EnrichmentRun {RunId} not found; aborting.", runId);
            return;
        }

        if (run.Status != EnrichmentStatus.Pending)
        {
            _logger.LogWarning(
                "EnrichmentCoordinator: EnrichmentRun {RunId} is in state {Status}, expected Pending; aborting.",
                runId, run.Status);
            return;
        }

        try
        {
            // Transition to Running
            run.Status = EnrichmentStatus.Running;
            await db.SaveChangesAsync(CancellationToken.None);

            // Incremental entry selection
            var lastFinishedAt = await GetLastCompletedFinishedAtAsync(db);
            var mediaItems = await GetEligibleMediaAsync(db, lastFinishedAt);

            _logger.LogInformation(
                "EnrichmentCoordinator: run {RunId} → {Count} eligible media items.", runId, mediaItems.Count);

            // Process each item
            var errors = new List<EnrichmentErrorDetailDto>();
            var mediaResults = new List<EnrichmentMediaResult>();
            var enrichedCount = 0;
            var failedCount = 0;
            var lastSave = DateTime.UtcNow;

            for (var i = 0; i < mediaItems.Count; i++)
            {
                var media = mediaItems[i];

                // Update CurrentItem for status reporting
                run.CurrentItem = $"{media.Title} (TMDB {media.TmdbId})";

                try
                {
                    // Fetch and map Media fields
                    await EnrichMediaFieldsAsync(db, tmdbService, media);

                    // Upsert TvSeason/TvEpisode for TV shows
                    if (media.Type == MediaType.TvShow)
                        await UpsertTvSeasonsAsync(db, tmdbService, media);

                    enrichedCount++;
                    mediaResults.Add(new EnrichmentMediaResult(media.Id, "Enriched"));

                    _logger.LogDebug(
                        "EnrichmentCoordinator: enriched media {MediaId} ({Title}).", media.Id, media.Title);
                }
                catch (Exception ex)
                {
                    // Per-entry error tracking — do NOT abort the batch
                    failedCount++;
                    errors.Add(new EnrichmentErrorDetailDto(media.Id, media.TmdbId, media.Title, ex.Message));
                    mediaResults.Add(new EnrichmentMediaResult(media.Id, "Failed"));

                    _logger.LogWarning(ex,
                        "EnrichmentCoordinator: failed to enrich media {MediaId} (TMDB {TmdbId}).",
                        media.Id, media.TmdbId);
                }

                // Persist progress every 10 entries or every 5 seconds
                var shouldSave = (i + 1) % 10 == 0
                                 || (DateTime.UtcNow - lastSave).TotalSeconds >= 5;

                if (shouldSave)
                {
                    run.EnrichedCount = enrichedCount;
                    run.FailedCount = failedCount;
                    run.ErrorDetailsJson = JsonSerializer.Serialize(errors);
                    run.EnrichedMediaIdsJson = JsonSerializer.Serialize(mediaResults);

                    try
                    {
                        await db.SaveChangesAsync(CancellationToken.None);
                        lastSave = DateTime.UtcNow;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "EnrichmentCoordinator: progress save failed at item {Index}.", i);
                    }
                }
            }

            // Transition to Completed
            run.Status = EnrichmentStatus.Completed;
            run.FinishedAt = DateTime.UtcNow;
            run.EnrichedCount = enrichedCount;
            run.FailedCount = failedCount;
            run.CurrentItem = null;
            run.ErrorDetailsJson = JsonSerializer.Serialize(errors);
            run.EnrichedMediaIdsJson = JsonSerializer.Serialize(mediaResults);

            _logger.LogInformation(
                "EnrichmentCoordinator: run {RunId} completed. Enriched={Enriched}, Failed={Failed}.",
                runId, enrichedCount, failedCount);
        }
        catch (Exception ex)
        {
            // Transition to Failed
            run.Status = EnrichmentStatus.Failed;
            run.FinishedAt = DateTime.UtcNow;
            run.FailureReason = ex.Message;

            _logger.LogError(ex, "EnrichmentCoordinator: run {RunId} failed fatally.", runId);
        }
        finally
        {
            try
            {
                await db.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "EnrichmentCoordinator: failed to persist final status for run {RunId}.", runId);
            }
        }
    }

    // =========================================================================
    // Incremental entry selection
    // =========================================================================

    private static async Task<DateTime?> GetLastCompletedFinishedAtAsync(MediaHandlerDbContext db)
    {
        return await db.EnrichmentRuns
            .AsNoTracking()
            .Where(r => r.Status == EnrichmentStatus.Completed && r.FinishedAt.HasValue)
            .OrderByDescending(r => r.FinishedAt)
            .Select(r => r.FinishedAt)
            .FirstOrDefaultAsync(CancellationToken.None);
    }

    private static async Task<List<Media>> GetEligibleMediaAsync(
        MediaHandlerDbContext db,
        DateTime? lastFinishedAt)
    {
        // Eligible: never-enriched (Overview IS NULL)
        //        OR  updated after the last completed enrichment run
        return await db.Medias
            .Include(m => m.Genres)
            .Include(m => m.TvSeasons)
                .ThenInclude(s => s.TvEpisodes)
            .Where(m => m.Overview == null
                        || (lastFinishedAt != null && m.UpdatedAt > lastFinishedAt))
            .ToListAsync(CancellationToken.None);
    }

    // =========================================================================
    // Media field population
    // =========================================================================

    private static async Task EnrichMediaFieldsAsync(
        MediaHandlerDbContext db,
        ITmdbService tmdbService,
        Media media)
    {
        var mediaTypeStr = media.Type == MediaType.TvShow ? "tv" : "movie";
        var details = await tmdbService.GetMediaDetailsAsync(
            media.TmdbId, mediaTypeStr, "en-US", CancellationToken.None);

        if (details is null)
            throw new InvalidOperationException(
                $"TMDB returned no details for {mediaTypeStr} id {media.TmdbId}.");

        // Map fields — Language (NOT OriginalLanguage per spec)
        media.Title = details.Title;
        media.OriginalTitle = details.OriginalTitle;
        media.Overview = details.Overview;
        media.ReleaseDate = details.ReleaseDate;
        media.Runtime = details.Runtime;
        media.PosterPath = details.PosterPath;
        media.BackdropPath = details.BackdropPath;
        media.VoteAverage = details.VoteAverage;
        media.VoteCount = details.VoteCount;
        media.Language = details.Language;
        media.Status = details.Status;

        // TV-only fields
        if (media.Type == MediaType.TvShow)
        {
            media.NumberOfSeasons = details.NumberOfSeasons;
            media.NumberOfEpisodes = details.NumberOfEpisodes;
        }

        // Upsert MediaGenre child records
        if (details.Genres is { Count: > 0 })
        {
            // Remove genres no longer returned by TMDB
            var incomingNames = details.Genres.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var toRemove = media.Genres
                .Where(g => !incomingNames.Contains(g.Name))
                .ToList();

            foreach (var genre in toRemove)
                db.MediaGenres.Remove(genre);

            // Add new genres
            var existingNames = media.Genres
                .Select(g => g.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var genreName in details.Genres)
            {
                if (!existingNames.Contains(genreName))
                {
                    media.Genres.Add(new MediaGenre
                    {
                        MediaId = media.Id,
                        Name = genreName
                    });
                }
            }
        }
        else
        {
            // No genres returned — clear existing entries
            foreach (var genre in media.Genres.ToList())
                db.MediaGenres.Remove(genre);
        }
    }

    // =========================================================================
    // TvSeason / TvEpisode upsert
    // =========================================================================

    private static async Task UpsertTvSeasonsAsync(
        MediaHandlerDbContext db,
        ITmdbService tmdbService,
        Media media)
    {
        var seasons = (await tmdbService.GetTvShowSeasonsAsync(
            media.TmdbId, "en-US", CancellationToken.None)).ToList();

        foreach (var seasonDto in seasons)
        {
            // Find or create TvSeason (match on SeasonNumber)
            var season = media.TvSeasons
                .FirstOrDefault(s => s.SeasonNumber == seasonDto.SeasonNumber);

            if (season is null)
            {
                season = new TvSeason
                {
                    MediaId = media.Id,
                    SeasonNumber = seasonDto.SeasonNumber,
                    Name = seasonDto.Name ?? $"Season {seasonDto.SeasonNumber}"
                };
                db.TvSeasons.Add(season);
                media.TvSeasons.Add(season);
            }
            else
            {
                season.Name = seasonDto.Name ?? $"Season {seasonDto.SeasonNumber}";
            }

            season.Overview = seasonDto.Overview;
            season.AirDate = seasonDto.AirDate;
            season.PosterPath = seasonDto.PosterPath;
            season.EpisodeCount = seasonDto.EpisodeCount;

            // Upsert episodes (match on EpisodeNumber)
            foreach (var episodeDto in seasonDto.Episodes)
            {
                var episode = season.TvEpisodes
                    .FirstOrDefault(e => e.EpisodeNumber == episodeDto.EpisodeNumber);

                if (episode is null)
                {
                    episode = new TvEpisode
                    {
                        SeasonId = season.Id,
                        EpisodeNumber = episodeDto.EpisodeNumber,
                        Name = episodeDto.Name ?? $"Episode {episodeDto.EpisodeNumber}"
                    };
                    db.TvEpisodes.Add(episode);
                    season.TvEpisodes.Add(episode);
                }
                else
                {
                    episode.Name = episodeDto.Name ?? $"Episode {episodeDto.EpisodeNumber}";
                }

                episode.Overview = episodeDto.Overview;
                episode.AirDate = episodeDto.AirDate;
                episode.StillPath = episodeDto.StillPath;
                episode.Runtime = episodeDto.Runtime;
            }
        }
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static EnrichmentRunDto MapToDto(Domain.Entities.EnrichmentRun run)
    {
        IReadOnlyList<EnrichmentErrorDetailDto> errorDetails = [];
        if (!string.IsNullOrEmpty(run.ErrorDetailsJson))
        {
            try
            {
                errorDetails = JsonSerializer.Deserialize<List<EnrichmentErrorDetailDto>>(run.ErrorDetailsJson)
                               ?? [];
            }
            catch
            {
                errorDetails = [];
            }
        }

        return new EnrichmentRunDto(
            EnrichmentRunId: run.Id,
            Status: run.Status,
            StartedAt: run.StartedAt,
            FinishedAt: run.FinishedAt,
            TotalItems: run.TotalItems,
            EnrichedCount: run.EnrichedCount,
            FailedCount: run.FailedCount,
            SkippedCount: run.SkippedCount,
            CurrentItem: run.CurrentItem,
            ErrorDetails: errorDetails);
    }
}

