// KodiImportPipeline — executes a single Kodi database import (or preview) run.
// Mirrors the ScanPipeline structure: bulk pre-loads (no per-item queries), deterministic
// processing order (music videos → movies → shows/episodes), batched counter persistence.
// Identity resolution precedence: saved admin resolution → Kodi TMDB id (no provider call) →
// non-TMDB external id via TMDB /find → title(+year) search via the existing TmdbMatcher policy.
// Links are never stolen: a file already linked to a different Media produces a reported
// conflict and the existing link is preserved (FR-017/FR-022).

using System.Text.Json;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models.Kodi;
using MediaHandler.Application.Common.Models.Scanner;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MediaHandler.Infrastructure.Kodi;

/// <summary>
///     Executes the Kodi import pipeline for a single <see cref="ImportRun" />.
///     Scoped service — resolved per run by <c>ImportRunCoordinator</c>, never injected into a singleton.
/// </summary>
public sealed class KodiImportPipeline(
    IApplicationDbContext db,
    IKodiVideoDbReader reader,
    ITmdbService tmdbService,
    ITmdbMatcher tmdbMatcher,
    ILogger<KodiImportPipeline> logger,
    IConfiguration? configuration = null)
{
    private const int SaveBatchSize = 50;
    private const int MaxUnmatchedPrefixes = 100;

    // =========================================================================
    // Public entry point
    // =========================================================================

    public async Task ExecuteAsync(
        ImportRun run,
        KodiImportStartParameters parameters,
        CancellationToken ct = default)
    {
        var snapshot = await reader.ReadAsync(parameters.StoredFilePath, parameters.SchemaVersion, ct);
        var state = await LoadStateAsync(run, parameters, ct);

        // Deterministic processing order: music videos → movies → shows/episodes.
        // This defines "first" for the Kodi-internal-duplicate rule: a file claimed as both
        // movie and episode keeps the movie link; the episode attempt is reported Conflict.
        foreach (var musicVideo in snapshot.MusicVideos)
        {
            state.Counters.TotalItems++;
            state.Counters.SkippedMusicVideos++;
            await RecordOutcomeAsync(run, state, new ImportItemOutcome
            {
                ImportRunId = run.Id,
                KodiItemKind = KodiItemKind.MusicVideo,
                KodiItemId = musicVideo.KodiMusicVideoId,
                Title = musicVideo.Title,
                MediaKind = null,
                Outcome = ImportItemStatus.SkippedMusicVideo,
                Reason = "Music videos are not imported (the app has no music-video media type)."
            }, ct);
        }

        foreach (var movie in snapshot.Movies.OrderBy(m => m.KodiMovieId))
            await ProcessMovieAsync(run, state, movie, parameters.Mappings, ct);

        foreach (var show in snapshot.Shows.OrderBy(s => s.KodiShowId))
            await ProcessShowAsync(run, state, show, parameters.Mappings, ct);

        SynthesizeNoLongerInKodi(run, state);

        state.Counters.ApplyTo(run);
        run.UnmatchedPrefixesJson = SerializePrefixes(state);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Kodi import run {ImportRunId} finished: {TotalItems} items, {Created} created, {Reused} reused, " +
            "{Unchanged} unchanged, {FilesLinked} files linked, {Conflicts} conflicts, {NeedsReview} needs review.",
            run.Id, state.Counters.TotalItems,
            state.Counters.MoviesCreated + state.Counters.ShowsCreated + state.Counters.EpisodesCreated,
            state.Counters.ItemsReused, state.Counters.ItemsUnchanged, state.Counters.FilesLinked,
            state.Counters.Conflicts, state.Counters.NeedsReview);
    }

    // =========================================================================
    // State pre-loads (bulk — no per-item queries)
    // =========================================================================

    private async Task<ImportState> LoadStateAsync(ImportRun run, KodiImportStartParameters parameters, CancellationToken ct)
    {
        var isPreview = parameters.Mode == KodiImportMode.Preview;
        var state = new ImportState { IsPreview = isPreview };

        // Preview pre-loads are read-only: in-run "would-link" state is mutated on untracked
        // entities only, so batched SaveChanges persists just the run and outcome rows.
        var files = await Tracking(db.MediaFiles, isPreview).ToListAsync(ct);
        foreach (var file in files)
        {
            if (!state.FilesByPath.TryAdd(file.FilePath, file))
                logger.LogWarning("Duplicate MediaFile path '{FilePath}' — first row wins.", file.FilePath);
        }

        var links = await Tracking(db.EpisodeFileLinks, isPreview).ToListAsync(ct);
        state.EpisodeLinksByFile = links
            .GroupBy(l => l.MediaFileId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var medias = await Tracking(db.Medias, isPreview).ToListAsync(ct);
        foreach (var media in medias)
        {
            // No unique constraint on (Type, TmdbId) exists today — first row wins, never a third.
            if (!state.MediaByKey.TryAdd((media.Type, media.TmdbId), media))
                logger.LogWarning(
                    "Duplicate Media rows for ({Type}, TmdbId {TmdbId}) — first row '{FirstId}' wins.",
                    media.Type, media.TmdbId, state.MediaByKey[(media.Type, media.TmdbId)].Id);

            state.MediaById.TryAdd(media.Id, media);
        }

        var seasons = await Tracking(db.TvSeasons, isPreview).ToListAsync(ct);
        foreach (var season in seasons)
        {
            state.SeasonsByKey.TryAdd((season.MediaId, season.SeasonNumber), season);
            state.SeasonsById.TryAdd(season.Id, season);
        }

        var episodes = await Tracking(db.TvEpisodes, isPreview).ToListAsync(ct);
        foreach (var episode in episodes)
        {
            state.EpisodesByKey.TryAdd((episode.SeasonId, episode.EpisodeNumber), episode);
            state.EpisodesById.TryAdd(episode.Id, episode);
        }

        var stackGroups = await Tracking(db.StackGroups, isPreview).ToListAsync(ct);
        foreach (var group in stackGroups)
            state.StackGroupsByMedia.TryAdd(group.MediaId, group);

        // Import-originated review items (resolved ones feed step 0 of identity resolution;
        // open ones prevent duplicate review rows).
        var importReviews = await db.ReviewItems
            .AsNoTracking()
            .Where(r => r.Source == ReviewItemSource.KodiImport
                        && (r.Status == ReviewStatus.Open || r.Status == ReviewStatus.Resolved))
            .ToListAsync(ct);

        state.ResolvedReviews = importReviews
            .Where(r => r.Status == ReviewStatus.Resolved && r.ResolvedTmdbId.HasValue)
            .GroupBy(r => r.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        state.OpenReviewKeys = importReviews
            .Where(r => r.Status == ReviewStatus.Open)
            .Select(r => r.FilePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Seen-before baseline: most recent completed Import run (failed runs and previews
        // never become baselines).
        var baselineRunId = await db.ImportRuns
            .AsNoTracking()
            .Where(r => r.Status == ImportRunStatus.Completed
                        && r.Mode == KodiImportMode.Import
                        && r.Id != run.Id)
            .OrderByDescending(r => r.FinishedAt)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(ct);

        if (baselineRunId.HasValue)
        {
            var baselineRows = await db.ImportItemOutcomes
                .AsNoTracking()
                .Where(o => o.ImportRunId == baselineRunId.Value
                            && o.KodiItemKind != KodiItemKind.MusicVideo
                            && o.Outcome != ImportItemStatus.NoLongerInKodi)
                .Select(o => new { o.KodiItemKind, o.KodiItemId, o.Title, o.MediaKind })
                .ToListAsync(ct);

            foreach (var row in baselineRows)
                state.Baseline.TryAdd((row.KodiItemKind, row.KodiItemId), (row.Title, row.MediaKind));
        }

        return state;
    }

    private static IQueryable<T> Tracking<T>(DbSet<T> set, bool asNoTracking) where T : class
    {
        return asNoTracking ? set.AsNoTracking() : set.AsTracking();
    }

    // =========================================================================
    // Movies
    // =========================================================================

    private async Task ProcessMovieAsync(
        ImportRun run,
        ImportState state,
        KodiMovieItem movie,
        IReadOnlyList<KodiPathMappingSnapshot> mappings,
        CancellationToken ct)
    {
        state.SeenKeys.Add((KodiItemKind.Movie, movie.KodiMovieId));
        state.Counters.TotalItems++;

        var itemKey = movie.FileRefs.Count > 0 ? movie.FileRefs[0] : $"kodi://movie/{movie.KodiMovieId}";
        var linkedMedia = FindLinkedMedia(state,
            movie.FileRefs.Count > 0 ? movie.FileRefs[0] : null, mappings);

        var resolution = await ResolveIdentityAsync(
            state, itemKey, movie.Title, movie.Year, MediaType.Film, movie.ExternalIds, ct);

        // US3-AC3 / FR-022 / D-C: identity discrepancy surfaced through an existing file link →
        // item-level Conflict; no new entry is created and no link is changed.
        if (resolution.Kind == IdentityResolutionKind.Resolved
            && linkedMedia is not null
            && (linkedMedia.Type != resolution.MediaKind!.Value || linkedMedia.TmdbId != resolution.TmdbId!.Value))
        {
            state.Counters.Conflicts++;
            await RecordOutcomeAsync(run, state, new ImportItemOutcome
            {
                ImportRunId = run.Id,
                KodiItemKind = KodiItemKind.Movie,
                KodiItemId = movie.KodiMovieId,
                Title = movie.Title,
                MediaKind = MediaType.Film,
                Outcome = ImportItemStatus.Conflict,
                Reason = $"Kodi now identifies this item as {resolution.MediaKind} TMDB {resolution.TmdbId}, " +
                         $"but its file is already linked to {linkedMedia.Type} TMDB {linkedMedia.TmdbId} " +
                         $"('{linkedMedia.Title}'). The existing link was preserved.",
                MediaId = linkedMedia.Id,
                MediaFileId = FindPrimaryFileId(state, movie.FileRefs[0], mappings)
            }, ct);
            return;
        }

        if (resolution.Kind != IdentityResolutionKind.Resolved)
        {
            await RecordIdentityFailureAsync(run, state, KodiItemKind.Movie, movie.KodiMovieId,
                movie.Title, MediaType.Film, movie.Year, itemKey, resolution, ct);
            return;
        }

        var key = (resolution.MediaKind!.Value, resolution.TmdbId!.Value);
        Media media;
        ImportItemStatus outcome;
        if (!state.MediaByKey.TryGetValue(key, out var existing))
        {
            media = new Media
            {
                TmdbId = key.Item2,
                Type = key.Item1,
                Title = movie.Title,
                OriginalTitle = movie.OriginalTitle,
                Year = movie.Year
            };
            if (!state.IsPreview)
                db.Medias.Add(media);
            state.MediaByKey[key] = media;
            state.MediaById[media.Id] = media;
            state.Counters.MoviesCreated++;
            outcome = ImportItemStatus.Created;
        }
        else
        {
            media = existing;
            if (state.Baseline.ContainsKey((KodiItemKind.Movie, movie.KodiMovieId)))
            {
                state.Counters.ItemsUnchanged++;
                outcome = ImportItemStatus.Unchanged;
            }
            else
            {
                state.Counters.ItemsReused++;
                outcome = ImportItemStatus.Reused;
            }
        }

        // Two Kodi items sharing one TMDB identity inside the same upload: both files are
        // linked to the single entry and the informational note is carried on the outcome.
        var informationalNote = state.ResolvedKeysThisRun.Add(key)
            ? null
            : "Duplicate TMDB identity within Kodi: another item in this upload resolves to the same entry.";

        var link = LinkMovieFiles(state, media, movie, mappings);
        ApplyLinkCounters(state, link);

        await RecordOutcomeAsync(run, state, new ImportItemOutcome
        {
            ImportRunId = run.Id,
            KodiItemKind = KodiItemKind.Movie,
            KodiItemId = movie.KodiMovieId,
            Title = movie.Title,
            MediaKind = MediaType.Film,
            Outcome = outcome,
            LinkOutcome = link.Outcome,
            LinkedFileCount = link.NewlyLinked,
            Reason = informationalNote ?? link.Reason,
            KodiPathPrefix = link.KodiPathPrefix,
            MediaId = media.Id,
            MediaFileId = link.PrimaryFileId
        }, ct);
    }

    private LinkComputation LinkMovieFiles(
        ImportState state,
        Media media,
        KodiMovieItem movie,
        IReadOnlyList<KodiPathMappingSnapshot> mappings)
    {
        var isStack = movie.FileRefs.Count > 1;
        var newlyLinked = 0;
        var alreadyLinked = 0;
        var failures = new List<(ImportLinkStatus Kind, string FileRef, string? Prefix)>();
        MediaFile? firstConflict = null;
        Guid? primaryFileId = null;

        for (var i = 0; i < movie.FileRefs.Count; i++)
        {
            var fileRef = movie.FileRefs[i];
            var translation = KodiPathTranslator.Translate(fileRef, mappings);

            if (translation.Kind == PathTranslationKind.UnsupportedScheme)
            {
                failures.Add((ImportLinkStatus.UnsupportedLocation, fileRef, null));
                continue;
            }

            if (translation.Kind == PathTranslationKind.NoMapping)
            {
                AddUnmatchedPrefix(state, translation.KodiDirectoryPrefix);
                failures.Add((ImportLinkStatus.UnmatchedPath, fileRef, translation.KodiDirectoryPrefix));
                continue;
            }

            if (!state.FilesByPath.TryGetValue(translation.TranslatedPath!, out var file))
            {
                failures.Add((ImportLinkStatus.NoScannedFile, fileRef, null));
                continue;
            }

            primaryFileId ??= file.Id;

            if (file.MediaId is null)
            {
                file.MediaId = media.Id;
                if (isStack)
                    AssignStackMembership(state, media, file, i);
                newlyLinked++;
            }
            else if (file.MediaId == media.Id)
            {
                if (isStack && file.StackGroupId is null)
                    AssignStackMembership(state, media, file, i);
                alreadyLinked++;
            }
            else
            {
                // Preserve, never steal (FR-017)
                firstConflict ??= file;
            }
        }

        if (firstConflict is not null)
        {
            return new LinkComputation(
                ImportLinkStatus.Conflict,
                newlyLinked,
                $"File '{firstConflict.FilePath}' is already linked to a different media entry; " +
                "the existing link was preserved.",
                null,
                primaryFileId);
        }

        if (failures.Count == 0)
        {
            return new LinkComputation(
                newlyLinked > 0 ? ImportLinkStatus.Linked : ImportLinkStatus.AlreadyLinked,
                newlyLinked, null, null, primaryFileId);
        }

        if (newlyLinked + alreadyLinked > 0)
        {
            var first = failures[0];
            return new LinkComputation(
                ImportLinkStatus.PartiallyLinked,
                newlyLinked,
                $"{failures.Count} of {movie.FileRefs.Count} part(s) could not be linked: " +
                DescribeFailure(first),
                first.Prefix,
                primaryFileId);
        }

        // No part linked at all — first failure kind in precedence
        // UnmatchedPath → UnsupportedLocation → NoScannedFile.
        var dominant = failures
            .OrderByDescending(f => f.Kind switch
            {
                ImportLinkStatus.UnmatchedPath => 3,
                ImportLinkStatus.UnsupportedLocation => 2,
                _ => 1
            })
            .First();

        return new LinkComputation(dominant.Kind, 0, DescribeFailure(dominant), dominant.Prefix, null);
    }

    private void AssignStackMembership(ImportState state, Media media, MediaFile part, int partIndex)
    {
        if (!state.StackGroupsByMedia.TryGetValue(media.Id, out var group))
        {
            group = new StackGroup { MediaId = media.Id };
            if (!state.IsPreview)
                db.StackGroups.Add(group);
            state.StackGroupsByMedia[media.Id] = group;
        }

        part.StackGroupId = group.Id;
        part.Role = partIndex == 0 ? MediaFileRole.Main : MediaFileRole.StackedPart;
    }

    // =========================================================================
    // TV shows + episodes
    // =========================================================================

    private async Task ProcessShowAsync(
        ImportRun run,
        ImportState state,
        KodiShowItem show,
        IReadOnlyList<KodiPathMappingSnapshot> mappings,
        CancellationToken ct)
    {
        state.SeenKeys.Add((KodiItemKind.TvShow, show.KodiShowId));
        state.Counters.TotalItems++;

        var itemKey = $"kodi://tvshow/{show.KodiShowId}";
        var orderedEpisodes = show.Episodes
            .OrderBy(e => e.SeasonNumber)
            .ThenBy(e => e.EpisodeNumber)
            .ToList();

        var resolution = await ResolveIdentityAsync(
            state, itemKey, show.Title, show.Year, MediaType.TvShow, show.ExternalIds, ct);

        if (resolution.Kind == IdentityResolutionKind.Resolved)
        {
            // Inspect every episode file, not just the first one: any already-linked episode
            // whose identity differs from the resolved show identity means the whole show is in conflict.
            var conflictingLinkedMedia = orderedEpisodes
                .Select(e => FindLinkedMedia(state, e.FileRef, mappings))
                .FirstOrDefault(m => m is not null
                                     && (m.Type != resolution.MediaKind!.Value || m.TmdbId != resolution.TmdbId!.Value));

            if (conflictingLinkedMedia is not null)
            {
                await RecordShowFailureAsync(run, state, show, orderedEpisodes, ImportItemStatus.Conflict,
                    $"Kodi now identifies this show as {resolution.MediaKind} TMDB {resolution.TmdbId}, " +
                    $"but an episode file is already linked to {conflictingLinkedMedia.Type} TMDB {conflictingLinkedMedia.TmdbId} " +
                    $"('{conflictingLinkedMedia.Title}'). The existing link was preserved.",
                    conflictCounter: true, ct);
                return;
            }
        }

        if (resolution.Kind != IdentityResolutionKind.Resolved)
        {
            var (failureOutcome, failureReason) = DescribeIdentityFailure(resolution);
            if (failureOutcome == ImportItemStatus.NeedsReview)
                EnsureReviewItem(state, itemKey, resolution.ReviewReason ?? ReviewReason.NoTmdbResult,
                    show.Title, show.Year, resolution.Candidates);
            await RecordShowFailureAsync(run, state, show, orderedEpisodes, failureOutcome, failureReason,
                failureOutcome == ImportItemStatus.Conflict, ct);
            return;
        }

        var key = (resolution.MediaKind!.Value, resolution.TmdbId!.Value);
        Media media;
        ImportItemStatus showOutcome;
        if (!state.MediaByKey.TryGetValue(key, out var existing))
        {
            media = new Media
            {
                TmdbId = key.Item2,
                Type = key.Item1,
                Title = show.Title,
                Year = show.Year
            };
            if (!state.IsPreview)
                db.Medias.Add(media);
            state.MediaByKey[key] = media;
            state.MediaById[media.Id] = media;
            state.Counters.ShowsCreated++;
            showOutcome = ImportItemStatus.Created;
        }
        else
        {
            media = existing;
            if (state.Baseline.ContainsKey((KodiItemKind.TvShow, show.KodiShowId)))
            {
                state.Counters.ItemsUnchanged++;
                showOutcome = ImportItemStatus.Unchanged;
            }
            else
            {
                state.Counters.ItemsReused++;
                showOutcome = ImportItemStatus.Reused;
            }
        }

        var informationalNote = state.ResolvedKeysThisRun.Add(key)
            ? null
            : "Duplicate TMDB identity within Kodi: another item in this upload resolves to the same entry.";

        await RecordOutcomeAsync(run, state, new ImportItemOutcome
        {
            ImportRunId = run.Id,
            KodiItemKind = KodiItemKind.TvShow,
            KodiItemId = show.KodiShowId,
            Title = show.Title,
            MediaKind = MediaType.TvShow,
            Outcome = showOutcome,
            LinkOutcome = null,
            LinkedFileCount = 0,
            Reason = informationalNote,
            MediaId = media.Id
        }, ct);

        // 1-based position of each episode among this run's episodes sharing the same file ref,
        // ordered by (season, episode) — multi-episode files keep their positions (US2-AC6).
        var positionsByEpisodeId = orderedEpisodes
            .GroupBy(e => e.FileRef, StringComparer.OrdinalIgnoreCase)
            .SelectMany(g => g.Select((episode, index) => (episode.KodiEpisodeId, Position: index + 1)))
            .ToDictionary(x => x.KodiEpisodeId, x => x.Position);

        foreach (var episodeItem in orderedEpisodes)
        {
            await ProcessEpisodeAsync(run, state, media, episodeItem,
                positionsByEpisodeId[episodeItem.KodiEpisodeId], mappings, ct);
        }
    }

    private async Task ProcessEpisodeAsync(
        ImportRun run,
        ImportState state,
        Media showMedia,
        KodiEpisodeItem episodeItem,
        int orderInFile,
        IReadOnlyList<KodiPathMappingSnapshot> mappings,
        CancellationToken ct)
    {
        state.SeenKeys.Add((KodiItemKind.Episode, episodeItem.KodiEpisodeId));
        state.Counters.TotalItems++;

        var season = GetOrCreateSeason(state, showMedia, episodeItem.SeasonNumber);
        var (episode, episodeCreated) = GetOrCreateEpisode(state, season, episodeItem);

        ImportItemStatus outcome;
        if (episodeCreated)
        {
            outcome = ImportItemStatus.Created;
        }
        else if (state.Baseline.ContainsKey((KodiItemKind.Episode, episodeItem.KodiEpisodeId)))
        {
            state.Counters.ItemsUnchanged++;
            outcome = ImportItemStatus.Unchanged;
        }
        else
        {
            state.Counters.ItemsReused++;
            outcome = ImportItemStatus.Reused;
        }

        var link = LinkEpisodeFile(state, showMedia, episode, episodeItem, orderInFile, mappings);
        ApplyLinkCounters(state, link);

        await RecordOutcomeAsync(run, state, new ImportItemOutcome
        {
            ImportRunId = run.Id,
            KodiItemKind = KodiItemKind.Episode,
            KodiItemId = episodeItem.KodiEpisodeId,
            Title = string.IsNullOrWhiteSpace(episodeItem.Title)
                ? $"S{episodeItem.SeasonNumber}E{episodeItem.EpisodeNumber}"
                : episodeItem.Title,
            MediaKind = MediaType.TvShow,
            Outcome = outcome,
            LinkOutcome = link.Outcome,
            LinkedFileCount = link.NewlyLinked,
            Reason = link.Reason,
            KodiPathPrefix = link.KodiPathPrefix,
            MediaId = showMedia.Id,
            MediaFileId = link.PrimaryFileId
        }, ct);
    }

    private LinkComputation LinkEpisodeFile(
        ImportState state,
        Media showMedia,
        TvEpisode episode,
        KodiEpisodeItem episodeItem,
        int orderInFile,
        IReadOnlyList<KodiPathMappingSnapshot> mappings)
    {
        var translation = KodiPathTranslator.Translate(episodeItem.FileRef, mappings);

        if (translation.Kind == PathTranslationKind.UnsupportedScheme)
        {
            return new LinkComputation(ImportLinkStatus.UnsupportedLocation, 0,
                "Non-filesystem location — the URI scheme cannot map to a scanned NAS file.",
                null, null);
        }

        if (translation.Kind == PathTranslationKind.NoMapping)
        {
            AddUnmatchedPrefix(state, translation.KodiDirectoryPrefix);
            return new LinkComputation(ImportLinkStatus.UnmatchedPath, 0,
                $"No path mapping covers Kodi prefix '{translation.KodiDirectoryPrefix}'.",
                translation.KodiDirectoryPrefix, null);
        }

        if (!state.FilesByPath.TryGetValue(translation.TranslatedPath!, out var file))
        {
            return new LinkComputation(ImportLinkStatus.NoScannedFile, 0,
                $"No scanned file matches the translated path '{translation.TranslatedPath}'.",
                null, null);
        }

        state.EpisodeLinksByFile.TryGetValue(file.Id, out var existingLinks);
        existingLinks ??= [];

        // Defensive: an episode link on this file belonging to another show → conflict, preserve.
        var foreignLink = existingLinks.FirstOrDefault(l =>
            OwningMediaId(state, l.TvEpisodeId) is Guid owner && owner != showMedia.Id);
        if (foreignLink is not null)
        {
            return new LinkComputation(ImportLinkStatus.Conflict, 0,
                $"File '{file.FilePath}' carries an episode link belonging to a different show; " +
                "the existing link was preserved.",
                null, file.Id);
        }

        if (file.MediaId is null)
        {
            file.MediaId = showMedia.Id;
            AddEpisodeLink(state, episode, file, orderInFile);
            return new LinkComputation(ImportLinkStatus.Linked, 1, null, null, file.Id);
        }

        if (file.MediaId == showMedia.Id)
        {
            // Ensure the link row exists — a manual link may have set MediaId without one.
            if (existingLinks.Any(l => l.TvEpisodeId == episode.Id))
                return new LinkComputation(ImportLinkStatus.AlreadyLinked, 0, null, null, file.Id);

            AddEpisodeLink(state, episode, file, orderInFile);
            return new LinkComputation(ImportLinkStatus.Linked, 1, null, null, file.Id);
        }

        return new LinkComputation(ImportLinkStatus.Conflict, 0,
            $"File '{file.FilePath}' is already linked to a different media entry; the existing link was preserved.",
            null, file.Id);
    }

    private Guid? OwningMediaId(ImportState state, Guid tvEpisodeId)
    {
        if (!state.EpisodesById.TryGetValue(tvEpisodeId, out var episode))
            return null;
        return state.SeasonsById.TryGetValue(episode.SeasonId, out var season) ? season.MediaId : null;
    }

    private void AddEpisodeLink(ImportState state, TvEpisode episode, MediaFile file, int orderInFile)
    {
        var link = new EpisodeFileLink
        {
            TvEpisodeId = episode.Id,
            MediaFileId = file.Id,
            OrderInFile = orderInFile
        };
        if (!state.IsPreview)
            db.EpisodeFileLinks.Add(link);

        if (!state.EpisodeLinksByFile.TryGetValue(file.Id, out var list))
            state.EpisodeLinksByFile[file.Id] = list = [];
        list.Add(link);
    }

    private TvSeason GetOrCreateSeason(ImportState state, Media media, int seasonNumber)
    {
        if (state.SeasonsByKey.TryGetValue((media.Id, seasonNumber), out var season))
            return season;

        // Name is set only on creation — enrichment owns it afterwards (FR-010).
        season = new TvSeason
        {
            MediaId = media.Id,
            SeasonNumber = seasonNumber,
            Name = $"Season {seasonNumber}"
        };
        if (!state.IsPreview)
            db.TvSeasons.Add(season);
        state.SeasonsByKey[(media.Id, seasonNumber)] = season;
        state.SeasonsById[season.Id] = season;
        return season;
    }

    private (TvEpisode Episode, bool Created) GetOrCreateEpisode(
        ImportState state, TvSeason season, KodiEpisodeItem episodeItem)
    {
        if (state.EpisodesByKey.TryGetValue((season.Id, episodeItem.EpisodeNumber), out var episode))
            return (episode, false);

        episode = new TvEpisode
        {
            SeasonId = season.Id,
            EpisodeNumber = episodeItem.EpisodeNumber,
            Name = string.IsNullOrWhiteSpace(episodeItem.Title)
                ? $"Episode {episodeItem.EpisodeNumber}"
                : episodeItem.Title
        };
        if (!state.IsPreview)
            db.TvEpisodes.Add(episode);
        state.EpisodesByKey[(season.Id, episodeItem.EpisodeNumber)] = episode;
        state.EpisodesById[episode.Id] = episode;
        state.Counters.EpisodesCreated++;
        return (episode, true);
    }

    /// <summary>
    ///     Records the show-level failure row plus one row per episode carrying the same outcome —
    ///     episodes of an unresolvable show cannot be materialized.
    /// </summary>
    private async Task RecordShowFailureAsync(
        ImportRun run,
        ImportState state,
        KodiShowItem show,
        IReadOnlyList<KodiEpisodeItem> orderedEpisodes,
        ImportItemStatus outcome,
        string reason,
        bool conflictCounter,
        CancellationToken ct)
    {
        IncrementFailureCounter(state, outcome, conflictCounter);

        await RecordOutcomeAsync(run, state, new ImportItemOutcome
        {
            ImportRunId = run.Id,
            KodiItemKind = KodiItemKind.TvShow,
            KodiItemId = show.KodiShowId,
            Title = show.Title,
            MediaKind = MediaType.TvShow,
            Outcome = outcome,
            Reason = reason
        }, ct);

        foreach (var episodeItem in orderedEpisodes)
        {
            state.SeenKeys.Add((KodiItemKind.Episode, episodeItem.KodiEpisodeId));
            state.Counters.TotalItems++;
            IncrementFailureCounter(state, outcome, conflictCounter);

            await RecordOutcomeAsync(run, state, new ImportItemOutcome
            {
                ImportRunId = run.Id,
                KodiItemKind = KodiItemKind.Episode,
                KodiItemId = episodeItem.KodiEpisodeId,
                Title = string.IsNullOrWhiteSpace(episodeItem.Title)
                    ? $"S{episodeItem.SeasonNumber}E{episodeItem.EpisodeNumber}"
                    : episodeItem.Title,
                MediaKind = MediaType.TvShow,
                Outcome = outcome,
                Reason = $"Owning show could not be resolved: {reason}"
            }, ct);
        }
    }

    private static void IncrementFailureCounter(ImportState state, ImportItemStatus outcome, bool conflict)
    {
        if (conflict || outcome == ImportItemStatus.Conflict)
            state.Counters.Conflicts++;
        else if (outcome == ImportItemStatus.NeedsReview)
            state.Counters.NeedsReview++;
        else if (outcome == ImportItemStatus.IdentityLookupFailed)
            state.Counters.IdentityLookupFailures++;
    }

    // =========================================================================
    // Identity resolution (FR-006 precedence chain)
    // =========================================================================

    private async Task<IdentityResolution> ResolveIdentityAsync(
        ImportState state,
        string itemKey,
        string title,
        int? year,
        MediaType kindHint,
        IReadOnlyList<KodiExternalId> externalIds,
        CancellationToken ct)
    {
        // Step 0 — saved admin resolution (mirrors the scanner's resolved-review reuse).
        if (state.ResolvedReviews.TryGetValue(itemKey, out var resolved) && resolved.ResolvedTmdbId.HasValue)
            return IdentityResolution.Resolved(resolved.ResolvedTmdbId.Value, resolved.ResolvedKind ?? kindHint);

        // Step 1 — Kodi TMDB id: used directly, no provider call (US1-AC2).
        var tmdbIdText = externalIds.FirstOrDefault(e => e.Provider == "tmdb")?.Value;
        if (tmdbIdText is not null && int.TryParse(tmdbIdText, out var tmdbId))
            return IdentityResolution.Resolved(tmdbId, kindHint);

        // Preview performs no provider traffic (US5-AC4).
        if (state.IsPreview)
            return IdentityResolution.RequiresLookup();

        // Step 2 — non-TMDB external id resolved through the provider.
        var external = externalIds.FirstOrDefault(e => e.Provider is "imdb" or "tvdb");
        if (external is not null)
        {
            var source = external.Provider == "imdb" ? "imdb_id" : "tvdb_id";
            TmdbIdLookupResult? found;
            try
            {
                found = await tmdbService.FindByExternalIdAsync(external.Value, source, kindHint, "en-US", ct);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex,
                    "TMDB external-id lookup failed for '{Title}' — the item is retried on the next run.", title);
                return IdentityResolution.LookupFailed();
            }

            if (found is not null)
                return IdentityResolution.Resolved(found.TmdbId, found.Kind);

            // null → fall through to the title search
        }

        // Step 3 — title(+year) search via the existing matcher (5% ambiguity policy,
        // ±1-year tolerance, per-run cache, transient-failure→NeedsReview).
        var searchLanguages =
            configuration?.GetSection("Scanner:DefaultSearchLanguages").Get<string[]>() is { Length: > 0 } cfg
                ? (IReadOnlyList<string>)cfg
                : null;

        var result = await tmdbMatcher.ResolveAsync(
            new MatchQuery(title, year, kindHint, Language: "en-US", SearchLanguages: searchLanguages), ct);

        if (result is { IsMatched: true, TmdbId: not null, Kind: not null })
            return IdentityResolution.Resolved(result.TmdbId.Value, result.Kind.Value);

        return IdentityResolution.NeedsReview(result.ReviewReason ?? ReviewReason.NoTmdbResult, result.Candidates);
    }

    private async Task RecordIdentityFailureAsync(
        ImportRun run,
        ImportState state,
        KodiItemKind itemKind,
        int kodiItemId,
        string title,
        MediaType mediaKind,
        int? year,
        string itemKey,
        IdentityResolution resolution,
        CancellationToken ct)
    {
        var (outcome, reason) = DescribeIdentityFailure(resolution);

        if (outcome == ImportItemStatus.NeedsReview)
            EnsureReviewItem(state, itemKey, resolution.ReviewReason ?? ReviewReason.NoTmdbResult,
                title, year, resolution.Candidates);

        IncrementFailureCounter(state, outcome, conflict: false);

        await RecordOutcomeAsync(run, state, new ImportItemOutcome
        {
            ImportRunId = run.Id,
            KodiItemKind = itemKind,
            KodiItemId = kodiItemId,
            Title = title,
            MediaKind = mediaKind,
            Outcome = outcome,
            Reason = reason
        }, ct);
    }

    private static (ImportItemStatus Outcome, string Reason) DescribeIdentityFailure(IdentityResolution resolution)
    {
        return resolution.Kind switch
        {
            IdentityResolutionKind.RequiresLookup => (
                ImportItemStatus.RequiresIdentityLookup,
                "Resolving this item requires a metadata provider lookup (not performed in preview mode)."),
            IdentityResolutionKind.LookupFailed => (
                ImportItemStatus.IdentityLookupFailed,
                "Identity lookup failed (provider unreachable) — the item is retried on the next run."),
            _ => (
                ImportItemStatus.NeedsReview,
                resolution.ReviewReason switch
                {
                    ReviewReason.MultipleCandidates =>
                        "Identity is ambiguous: several comparable provider candidates were found.",
                    ReviewReason.YearMismatch =>
                        "The single provider candidate's year differs from Kodi's by more than one year.",
                    _ => "No confident provider identity could be resolved for this item."
                })
        };
    }

    private void EnsureReviewItem(
        ImportState state,
        string itemKey,
        ReviewReason reason,
        string title,
        int? year,
        IReadOnlyList<TmdbCandidate>? candidates)
    {
        // Reuse an existing open item for the same Kodi URI (filtered unique index backstop).
        if (!state.OpenReviewKeys.Add(itemKey))
            return;

        if (state.IsPreview)
            return;

        db.ReviewItems.Add(new ReviewItem
        {
            FilePath = itemKey,
            Reason = reason,
            Status = ReviewStatus.Open,
            Source = ReviewItemSource.KodiImport,
            ParsedTitle = title,
            ParsedYear = year,
            CandidatesJson = SerializeCandidates(candidates),
            FirstSeenScanRunId = null
        });
    }

    // =========================================================================
    // Baseline diff — items no longer in Kodi (FR-021)
    // =========================================================================

    private void SynthesizeNoLongerInKodi(ImportRun run, ImportState state)
    {
        foreach (var ((kind, kodiItemId), (title, mediaKind)) in state.Baseline)
        {
            if (state.SeenKeys.Contains((kind, kodiItemId)))
                continue;

            state.Counters.NoLongerInKodi++;
            db.ImportItemOutcomes.Add(new ImportItemOutcome
            {
                ImportRunId = run.Id,
                KodiItemKind = kind,
                KodiItemId = kodiItemId,
                Title = title,
                MediaKind = mediaKind,
                Outcome = ImportItemStatus.NoLongerInKodi,
                Reason = "Present in the previous completed import but absent from this upload; " +
                         "app data was left untouched."
            });
        }
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private Media? FindLinkedMedia(ImportState state, string? fileRef, IReadOnlyList<KodiPathMappingSnapshot> mappings)
    {
        var file = FindLinkedFile(state, fileRef, mappings);
        if (file?.MediaId is null)
            return null;
        return state.MediaById.TryGetValue(file.MediaId!.Value, out var media) ? media : null;
    }

    private Guid? FindPrimaryFileId(ImportState state, string? fileRef, IReadOnlyList<KodiPathMappingSnapshot> mappings)
    {
        return FindLinkedFile(state, fileRef, mappings)?.Id;
    }

    private MediaFile? FindLinkedFile(ImportState state, string? fileRef, IReadOnlyList<KodiPathMappingSnapshot> mappings)
    {
        if (string.IsNullOrEmpty(fileRef))
            return null;
        var translation = KodiPathTranslator.Translate(fileRef, mappings);
        if (translation.Kind != PathTranslationKind.Translated)
            return null;
        return state.FilesByPath.TryGetValue(translation.TranslatedPath!, out var file) ? file : null;
    }

    private void ApplyLinkCounters(ImportState state, LinkComputation link)
    {
        state.Counters.FilesLinked += link.NewlyLinked;
        switch (link.Outcome)
        {
            case ImportLinkStatus.UnmatchedPath:
                state.Counters.UnmatchedPaths++;
                break;
            case ImportLinkStatus.NoScannedFile:
                state.Counters.NoScannedFiles++;
                break;
            case ImportLinkStatus.UnsupportedLocation:
                state.Counters.UnsupportedLocations++;
                break;
            case ImportLinkStatus.Conflict:
                state.Counters.Conflicts++;
                break;
        }
    }

    private static void AddUnmatchedPrefix(ImportState state, string? prefix)
    {
        if (prefix is not null && state.UnmatchedPrefixes.Count < MaxUnmatchedPrefixes)
            state.UnmatchedPrefixes.Add(prefix);
    }

    private static string DescribeFailure((ImportLinkStatus Kind, string FileRef, string? Prefix) failure)
    {
        return failure.Kind switch
        {
            ImportLinkStatus.UnmatchedPath =>
                $"no path mapping covers Kodi prefix '{failure.Prefix}'",
            ImportLinkStatus.UnsupportedLocation =>
                $"non-filesystem location '{failure.FileRef}'",
            _ =>
                $"no scanned file matches '{failure.FileRef}'"
        };
    }

    private async Task RecordOutcomeAsync(ImportRun run, ImportState state, ImportItemOutcome outcome, CancellationToken ct)
    {
        db.ImportItemOutcomes.Add(outcome);
        state.ItemsSinceLastSave++;

        if (state.ItemsSinceLastSave >= SaveBatchSize)
        {
            state.Counters.ApplyTo(run);
            run.UnmatchedPrefixesJson = SerializePrefixes(state);
            await db.SaveChangesAsync(ct);
            state.ItemsSinceLastSave = 0;
        }
    }

    private static string SerializePrefixes(ImportState state)
    {
        return JsonSerializer.Serialize(state.UnmatchedPrefixes.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
    }

    private static string SerializeCandidates(IReadOnlyList<TmdbCandidate>? candidates)
    {
        if (candidates is null or { Count: 0 })
            return "[]";

        return JsonSerializer.Serialize(
            candidates.Select(c => new
            {
                tmdbId = c.TmdbId,
                kind = c.Kind.ToString(),
                title = c.Title,
                year = c.Year,
                score = c.Score,
                posterPath = c.PosterPath
            }));
    }

    // =========================================================================
    // Inner types
    // =========================================================================

    private enum IdentityResolutionKind
    {
        Resolved,
        NeedsReview,
        LookupFailed,
        RequiresLookup
    }

    private sealed record IdentityResolution(
        IdentityResolutionKind Kind,
        int? TmdbId,
        MediaType? MediaKind,
        ReviewReason? ReviewReason,
        IReadOnlyList<TmdbCandidate>? Candidates)
    {
        public static IdentityResolution Resolved(int tmdbId, MediaType kind)
        {
            return new IdentityResolution(IdentityResolutionKind.Resolved, tmdbId, kind, null, null);
        }

        public static IdentityResolution NeedsReview(ReviewReason reason, IReadOnlyList<TmdbCandidate> candidates)
        {
            return new IdentityResolution(IdentityResolutionKind.NeedsReview, null, null, reason, candidates);
        }

        public static IdentityResolution LookupFailed()
        {
            return new IdentityResolution(IdentityResolutionKind.LookupFailed, null, null, null, null);
        }

        public static IdentityResolution RequiresLookup()
        {
            return new IdentityResolution(IdentityResolutionKind.RequiresLookup, null, null, null, null);
        }
    }

    private sealed record LinkComputation(
        ImportLinkStatus? Outcome,
        int NewlyLinked,
        string? Reason,
        string? KodiPathPrefix,
        Guid? PrimaryFileId);

    private sealed class ImportState
    {
        public bool IsPreview { get; init; }
        public ImportCounters Counters { get; } = new();
        public Dictionary<string, MediaFile> FilesByPath { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<Guid, List<EpisodeFileLink>> EpisodeLinksByFile { get; set; } = [];
        public Dictionary<(MediaType Type, int TmdbId), Media> MediaByKey { get; } = [];
        public Dictionary<Guid, Media> MediaById { get; } = [];
        public Dictionary<(Guid MediaId, int SeasonNumber), TvSeason> SeasonsByKey { get; } = [];
        public Dictionary<Guid, TvSeason> SeasonsById { get; } = [];
        public Dictionary<(Guid SeasonId, int EpisodeNumber), TvEpisode> EpisodesByKey { get; } = [];
        public Dictionary<Guid, TvEpisode> EpisodesById { get; } = [];
        public Dictionary<Guid, StackGroup> StackGroupsByMedia { get; } = [];
        public Dictionary<string, ReviewItem> ResolvedReviews { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> OpenReviewKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<(KodiItemKind Kind, int Id), (string Title, MediaType? MediaKind)> Baseline { get; } = [];
        public HashSet<(KodiItemKind Kind, int Id)> SeenKeys { get; } = [];
        public HashSet<(MediaType Type, int TmdbId)> ResolvedKeysThisRun { get; } = [];
        public HashSet<string> UnmatchedPrefixes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int ItemsSinceLastSave { get; set; }
    }

    private sealed class ImportCounters
    {
        public int TotalItems;
        public int MoviesCreated;
        public int ShowsCreated;
        public int EpisodesCreated;
        public int ItemsReused;
        public int ItemsUnchanged;
        public int FilesLinked;
        public int UnmatchedPaths;
        public int NoScannedFiles;
        public int UnsupportedLocations;
        public int Conflicts;
        public int NoLongerInKodi;
        public int NeedsReview;
        public int IdentityLookupFailures;
        public int SkippedMusicVideos;

        public void ApplyTo(ImportRun run)
        {
            run.TotalItems = TotalItems;
            run.MoviesCreated = MoviesCreated;
            run.ShowsCreated = ShowsCreated;
            run.EpisodesCreated = EpisodesCreated;
            run.ItemsReused = ItemsReused;
            run.ItemsUnchanged = ItemsUnchanged;
            run.FilesLinked = FilesLinked;
            run.UnmatchedPaths = UnmatchedPaths;
            run.NoScannedFiles = NoScannedFiles;
            run.UnsupportedLocations = UnsupportedLocations;
            run.Conflicts = Conflicts;
            run.NoLongerInKodi = NoLongerInKodi;
            run.NeedsReview = NeedsReview;
            run.IdentityLookupFailures = IdentityLookupFailures;
            run.SkippedMusicVideos = SkippedMusicVideos;
        }
    }
}
