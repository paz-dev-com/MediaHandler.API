// ScanPipeline — orchestrates the full scanner pipeline:
// enumerate → exclude → group(stacks) → parse → NFO-lookup → classify → TMDB-match → fingerprint → persist.
// Files that cannot be unambiguously matched to TMDB become ReviewItems rather than silent mis-mappings.
// NFO sidecar files (movie.nfo, tvshow.nfo, <basename>.nfo) are discovered from the enumerated entry
// list, parsed by INfoParser, and their TmdbId fed into the TMDB precedence chain at highest priority.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models.Scanner;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MediaHandler.Infrastructure.Nas.Scanner;

/// <summary>
///     Executes the full scanner pipeline for a single <see cref="ScanRun" />.
///     Pipeline stages: enumerate → exclude → group(stacks) → parse → NFO lookup → classify → fingerprint → persist.
/// </summary>
public sealed class ScanPipeline(
    IApplicationDbContext db,
    INasFileEnumerator enumerator,
    IExclusionEvaluator exclusionEvaluator,
    IStackingDetector stackingDetector,
    IKodiNameParser nameParser,
    ITvEpisodeMatcher episodeMatcher,
    ITmdbMatcher tmdbMatcher,
    ILogger<ScanPipeline> logger,
    INfoParser? nfoParser = null,
    IConfiguration? configuration = null)
{
    // =========================================================================
    // Public entry point
    // =========================================================================

    public async Task ExecuteAsync(
        ScanRun scanRun,
        IReadOnlyList<LibraryRoot> roots,
        ChannelWriter<ScanProgressDto> progress,
        string? language = null,
        CancellationToken ct = default)
    {
        var counters = new ScanCounters();

        // Track which roots failed enumeration so removed-file detection is suppressed for them.
        // A failed root (NAS unreachable) must NOT trigger mass-removal of its files — the absence
        // of entries would be a transient network issue, not a genuine removal.
        var failedRootIds = new HashSet<Guid>();

        try
        {
            foreach (var root in roots)
            {
                ct.ThrowIfCancellationRequested();
                logger.LogDebug("Stage transition: starting root processing for {RootId} ({Path})",
                    root.Id, root.Path);
                var succeeded = await ProcessRootAsync(scanRun, root, counters, progress, language, ct);
                if (!succeeded)
                    failedRootIds.Add(root.Id);
            }

            // Only run removed-file detection for roots that were successfully enumerated.
            // Roots that failed enumeration already have a "NAS unreachable" decision written by
            // ProcessRootAsync and must not cause their files to be falsely marked as removed.
            var successfulRoots = roots.Where(r => !failedRootIds.Contains(r.Id)).ToList();
            logger.LogDebug("Stage transition: marking removed files for {Count} successful roots",
                successfulRoots.Count);
            await MarkRemovedFilesAsync(scanRun, successfulRoots, counters, ct);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Scan run {ScanRunId} was cancelled.", scanRun.Id);
            throw;
        }

        // Persist final counters
        scanRun.TotalDiscovered = counters.TotalDiscovered;
        scanRun.Added = counters.Added;
        scanRun.Updated = counters.Updated;
        scanRun.Unchanged = counters.Unchanged;
        scanRun.Removed = counters.Removed;
        scanRun.Excluded = counters.Excluded;
        scanRun.NeedsReview = counters.NeedsReview;
        await db.SaveChangesAsync(ct);

        logger.LogDebug("Stage transition: scan pipeline completed for {ScanRunId}", scanRun.Id);

        await progress.WriteAsync(new ScanProgressDto(
            scanRun.Id, "Completed",
            counters.TotalDiscovered, counters.TotalDiscovered,
            null, null), ct);
    }

    // =========================================================================
    // Per-root processing
    // =========================================================================

    /// <summary>
    ///     Processes one library root through the full pipeline.
    ///     Returns <c>true</c> when the root was enumerated successfully, <c>false</c> when
    ///     the NAS was unreachable (partial failure — a "NAS unreachable" decision is written
    ///     but removed-file detection for this root is suppressed by the caller).
    /// </summary>
    private async Task<bool> ProcessRootAsync(
        ScanRun scanRun,
        LibraryRoot root,
        ScanCounters counters,
        ChannelWriter<ScanProgressDto> progress,
        string? language,
        CancellationToken ct)
    {
        logger.LogDebug("Stage transition: enumerating files for root {RootId} ({Path})",
            root.Id, root.Path);

        // Collect all file entries from the NAS (enumeration stage)
        var allEntries = new List<NasFileEntry>();
        try
        {
            await foreach (var entry in enumerator.EnumerateAsync(root, ct))
                allEntries.Add(entry);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "NAS enumeration failed for root {RootId} ({Path}). Skipping removed-file detection for this root.",
                root.Id, root.Path);
            // NAS unreachable: write a single diagnostic decision so the admin can see the failure.
            // Returning false signals to the caller that removed-file detection must be skipped
            // for this root to avoid falsely marking its files as removed.
            await db.ScanItemDecisions.AddAsync(new ScanItemDecision
            {
                ScanRunId = scanRun.Id,
                FilePath = root.Path,
                Kind = ScanDecisionKind.NeedsReview,
                Reason = "NAS unreachable",
                RuleId = null,
                LibraryRootId = root.Id
            }, ct);
            await db.SaveChangesAsync(ct);
            return false;
        }

        // Build .nomedia folder set
        var nomediaFolders = allEntries
            .Where(e => string.Equals(e.FileName, ".nomedia", StringComparison.OrdinalIgnoreCase))
            .Select(e => Path.GetDirectoryName(e.AbsolutePath) ?? string.Empty)
            .Where(d => !string.IsNullOrEmpty(d))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var exclusionCtx = new ExclusionContext(root, KodiRegexCatalog.DefaultExclusionRules, nomediaFolders);

        // Build NFO lookup from enumerated entries
        // NFO files are discovered during enumeration and looked up when each video
        // file is processed. Two lookup levels:
        //   • Per-file: "<videoBasename>.nfo" in the same folder (e.g., "Inception (2010).nfo")
        //   • Per-folder: "movie.nfo" or "tvshow.nfo" in the same folder
        // SOURCE: Kodi wiki — https://kodi.wiki/view/NFO_files describing the sidecar placement rules
        var nfoEntriesByPath = nfoParser is not null
            ? allEntries
                .Where(e => !e.IsDirectory
                            && string.Equals(e.Extension, "nfo", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(e => e.AbsolutePath, StringComparer.OrdinalIgnoreCase)
            : [];

        // Exclusion stage
        logger.LogDebug("Stage transition: exclusion evaluation for root {RootId}", root.Id);
        var videoFiles = new List<NasFileEntry>();
        foreach (var entry in allEntries)
        {
            counters.TotalDiscovered++;
            var verdict = exclusionEvaluator.Evaluate(entry, exclusionCtx);
            if (verdict.IsExcluded)
            {
                counters.Excluded++;
                if (!entry.IsDirectory) // only create decisions for actual files
                {
                    await db.ScanItemDecisions.AddAsync(new ScanItemDecision
                    {
                        ScanRunId = scanRun.Id,
                        FilePath = entry.AbsolutePath,
                        Kind = ScanDecisionKind.Excluded,
                        Reason = verdict.Reason,
                        RuleId = verdict.RuleId,
                        LibraryRootId = root.Id
                    }, ct);

                    // Structured per-file decision log
                    logger.LogInformation(
                        "Scan decision: {ScanRunId} | {FilePath} | Kind={Kind} | Reason={Reason} | RuleId={RuleId}",
                        scanRun.Id, entry.AbsolutePath, ScanDecisionKind.Excluded, verdict.Reason, verdict.RuleId);
                }

                continue;
            }

            videoFiles.Add(entry);
        }

        // Pre-load resolved ReviewItems for this batch of paths
        var videoPaths = videoFiles.Select(f => f.AbsolutePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var videoPathsList = videoPaths.ToList(); // List<string> is reliably translated to SQL IN clause

        var resolvedReviewItems = await db.ReviewItems
            .Where(r => r.Status == ReviewStatus.Resolved
                        && r.ResolvedTmdbId.HasValue
                        && videoPathsList.Contains(r.FilePath))
            .ToDictionaryAsync(r => r.FilePath, r => r, StringComparer.OrdinalIgnoreCase, ct);

        // Pre-load existing open ReviewItems to avoid per-file duplicate checks
        var existingOpenReviewPaths = (await db.ReviewItems
                .Where(r => r.Status == ReviewStatus.Open
                            && videoPathsList.Contains(r.FilePath))
                .Select(r => r.FilePath)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Pre-load existing MediaFiles for this root by LibraryRootId (indexed) — much faster than IN clause
        var existingMediaFiles = await db.MediaFiles
            .Where(mf => mf.LibraryRootId == root.Id)
            .ToDictionaryAsync(mf => mf.FilePath, mf => mf, StringComparer.OrdinalIgnoreCase, ct);

        // Group by folder for stacking detection
        logger.LogDebug("Stage transition: stacking detection for root {RootId}", root.Id);
        var byFolder = videoFiles
            .GroupBy(f => Path.GetDirectoryName(f.AbsolutePath) ?? string.Empty);

        var stackedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allStacks = new List<StackGroupCandidate>();

        foreach (var folderGroup in byFolder)
        {
            var stacks = stackingDetector.Group(folderGroup);
            foreach (var stack in stacks)
            {
                var stackGroup = new StackGroup
                {
                    MediaId = Guid.Empty // placeholder; will be assigned in persist stage
                };

                allStacks.Add(stack);
                foreach (var part in stack.Parts)
                    stackedPaths.Add(part.AbsolutePath);
            }
        }

        // Classification & persistence stage
        logger.LogDebug("Stage transition: classification and persistence for root {RootId}", root.Id);
        await EmitProgressAsync(scanRun.Id, "Classifying", 0, videoFiles.Count, null, progress, ct);

        // Track paths added in this batch to handle duplicate entries in the source (defensive check)
        var inFlightPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var processedInRoot = 0;
        foreach (var file in videoFiles)
        {
            ct.ThrowIfCancellationRequested();
            processedInRoot++;

            var stack = allStacks.FirstOrDefault(s => s.Parts.Any(p => p.AbsolutePath == file.AbsolutePath));
            var isStackedPart = stackedPaths.Contains(file.AbsolutePath);
            var partIndex = stack is null
                ? 0
                : stack.Parts.TakeWhile(p => p.AbsolutePath != file.AbsolutePath).Count();
            var role = isStackedPart && partIndex > 0
                ? MediaFileRole.StackedPart
                : MediaFileRole.Main;

            await ClassifyAndPersistFileAsync(
                scanRun, root, file, role, stack, counters, resolvedReviewItems, existingOpenReviewPaths,
                existingMediaFiles, inFlightPaths, nfoEntriesByPath, language, ct);

            if (processedInRoot % 10 == 0)
            {
                scanRun.TotalDiscovered = counters.TotalDiscovered;
                scanRun.Added = counters.Added;
                scanRun.Updated = counters.Updated;
                scanRun.Unchanged = counters.Unchanged;
                scanRun.Removed = counters.Removed;
                scanRun.Excluded = counters.Excluded;
                scanRun.NeedsReview = counters.NeedsReview;
                await db.SaveChangesAsync(ct);
            }

            if (processedInRoot % 50 == 0)
                await EmitProgressAsync(scanRun.Id, "Classifying", processedInRoot, videoFiles.Count,
                    file.AbsolutePath, progress, ct);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Root processing complete: {ScanRunId} | Root={RootId} | Added={Added} | Updated={Updated} | Unchanged={Unchanged} | Excluded={Excluded} | NeedsReview={NeedsReview}",
            scanRun.Id, root.Id, counters.Added, counters.Updated, counters.Unchanged, counters.Excluded,
            counters.NeedsReview);

        return true;
    }

    // =========================================================================
    // File classification + NFO lookup + TMDB matching + persistence
    // =========================================================================

    private async Task ClassifyAndPersistFileAsync(
        ScanRun scanRun,
        LibraryRoot root,
        NasFileEntry file,
        MediaFileRole role,
        StackGroupCandidate? stack,
        ScanCounters counters,
        Dictionary<string, ReviewItem> resolvedReviewItems,
        HashSet<string> existingOpenReviewPaths,
        Dictionary<string, MediaFile> existingMediaFiles,
        HashSet<string> inFlightPaths,
        Dictionary<string, NasFileEntry> nfoEntriesByPath,
        string? language,
        CancellationToken ct)
    {
        // Guard against duplicate paths within the same batch (defensive handling of fixture bugs or NAS oddities)
        if (inFlightPaths.Contains(file.AbsolutePath))
        {
            logger.LogDebug("Skipping duplicate path in batch: {FilePath}", file.AbsolutePath);
            return;
        }

        var fingerprint = ComputeFingerprint(file.AbsolutePath, file.SizeBytes, file.MtimeUtc);

        // Check for existing MediaFile by path (incremental idempotency) — use pre-loaded batch
        if (existingMediaFiles.TryGetValue(file.AbsolutePath, out var existing))
        {
            if (existing.Fingerprint == fingerprint)
            {
                // Unchanged
                existing.LastSeenScanRunId = scanRun.Id;
                counters.Unchanged++;
                await db.ScanItemDecisions.AddAsync(new ScanItemDecision
                {
                    ScanRunId = scanRun.Id,
                    FilePath = file.AbsolutePath,
                    Kind = ScanDecisionKind.Unchanged,
                    MediaFileId = existing.Id,
                    LibraryRootId = root.Id
                }, ct);

                logger.LogInformation(
                    "Scan decision: {ScanRunId} | {FilePath} | Kind={Kind}",
                    scanRun.Id, file.AbsolutePath, ScanDecisionKind.Unchanged);
                return;
            }

            // Updated (fingerprint changed — size or mtime changed)
            existing.Fingerprint = fingerprint;
            existing.MtimeUtc = file.MtimeUtc;
            existing.FileSizeBytes = file.SizeBytes;
            existing.LastSeenScanRunId = scanRun.Id;
            existing.MissingSince = null;
            counters.Updated++;
            await db.ScanItemDecisions.AddAsync(new ScanItemDecision
            {
                ScanRunId = scanRun.Id,
                FilePath = file.AbsolutePath,
                Kind = ScanDecisionKind.Updated,
                MediaFileId = existing.Id,
                LibraryRootId = root.Id
            }, ct);

            logger.LogInformation(
                "Scan decision: {ScanRunId} | {FilePath} | Kind={Kind}",
                scanRun.Id, file.AbsolutePath, ScanDecisionKind.Updated);
            return;
        }

        // Determine classification (movie vs episode)
        var isFromTvRoot = root.Kind is LibraryRootKind.TvShows;
        var hint = BuildEpisodeHint(file.AbsolutePath);
        var episodeNumbers = !isFromTvRoot
            ? (IReadOnlyList<EpisodeNumber>)[]
            : episodeMatcher.Match(file.FileName, hint);

        if (isFromTvRoot && episodeNumbers.Count > 0)
            role = MediaFileRole.Episode;
        else if (root.Kind == LibraryRootKind.Movies || episodeNumbers.Count == 0)
            role = role == MediaFileRole.StackedPart ? MediaFileRole.StackedPart : MediaFileRole.Main;

        // NFO lookup and parsing
        // Discover the most authoritative NFO sidecar for this file:
        //   1. Per-file NFO: "<videoBasename>.nfo" in the same folder (highest precedence)
        //   2. Per-folder NFO: "movie.nfo" or "tvshow.nfo" in the same folder
        // SOURCE: Kodi wiki — NFO file discovery priority follows the same order Kodi uses.
        NfoParseResult? nfoResult = null;
        string? nfoPath = null;

        if (nfoParser is not null)
        {
            var folder = Path.GetDirectoryName(file.AbsolutePath) ?? string.Empty;
            var baseName = Path.GetFileNameWithoutExtension(file.FileName);

            // Per-file NFO has highest priority
            var perFileNfoPath = Path.Combine(folder, baseName + ".nfo");
            if (nfoEntriesByPath.ContainsKey(perFileNfoPath))
                nfoPath = perFileNfoPath;

            // Per-folder fallbacks: movie.nfo in the same folder
            if (nfoPath is null)
            {
                var movieNfoPath = Path.Combine(folder, "movie.nfo");
                if (nfoEntriesByPath.ContainsKey(movieNfoPath))
                    nfoPath = movieNfoPath;
            }

            // tvshow.nfo: check the same folder and, for season subfolders, also the parent folder.
            // SOURCE: Kodi wiki — tvshow.nfo lives at the show root, not inside each season folder.
            if (nfoPath is null)
            {
                var tvShowNfoPath = Path.Combine(folder, "tvshow.nfo");
                if (nfoEntriesByPath.ContainsKey(tvShowNfoPath))
                {
                    nfoPath = tvShowNfoPath;
                }
                else
                {
                    // Walk up one level to find tvshow.nfo at the show root
                    // (episode files typically live in Season X subdirectories)
                    var parentFolder = Path.GetDirectoryName(folder);
                    if (parentFolder is not null)
                    {
                        var parentTvShowNfoPath = Path.Combine(parentFolder, "tvshow.nfo");
                        if (nfoEntriesByPath.ContainsKey(parentTvShowNfoPath))
                            nfoPath = parentTvShowNfoPath;
                    }
                }
            }

            // Parse the NFO if one was found
            if (nfoPath is not null)
                try
                {
                    nfoResult = await nfoParser.ParseAsync(nfoPath, ct);

                    if (nfoResult.ParsedSuccessfully)
                    {
                        logger.LogDebug(
                            "NFO sidecar parsed for '{FilePath}': title='{Title}' year={Year} tmdbId={TmdbId}",
                            file.AbsolutePath, nfoResult.Title, nfoResult.Year, nfoResult.TmdbId);

                        // Persist the NfoMetadata row (upsert by SourcePath to handle incremental rescans)
                        await PersistNfoMetadataAsync(nfoPath, nfoResult, ct);
                    }
                    else
                    {
                        logger.LogWarning(
                            "NFO sidecar at '{NfoPath}' is malformed for file '{FilePath}': {Warning}",
                            nfoPath, file.AbsolutePath, nfoResult.Warning);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Unexpected error parsing NFO sidecar at '{NfoPath}'", nfoPath);
                    nfoResult = NfoParseResult.Malformed(ex.Message);
                }
        }

        // Build the TMDB match query from parsed name data (and NFO metadata when available).
        // Done here (before the resolved-review check) so all downstream branches have access.
        var matchQuery = BuildMatchQuery(file, root, episodeNumbers, role, nfoResult, language);

        // TMDB resolution stage
        // Check for a previously resolved ReviewItem for this path first.
        // If found, skip the TMDB title search and re-use the administrator's saved mapping.
        if (resolvedReviewItems.TryGetValue(file.AbsolutePath, out var resolvedItem)
            && resolvedItem.ResolvedTmdbId.HasValue)
        {
            logger.LogDebug(
                "Re-using saved resolution (TmdbId={TmdbId}) for '{FilePath}'.",
                resolvedItem.ResolvedTmdbId.Value, file.AbsolutePath);

            inFlightPaths.Add(file.AbsolutePath);
            await PersistNewMediaFileAsync(
                scanRun, root, file, role, fingerprint, counters,
                resolvedItem.ResolvedTmdbId, resolvedItem.ResolvedKind,
                "[]",
                matchQuery.Title, matchQuery.Year,
                episodeNumbers.Count > 0 ? episodeNumbers[0].Season : (int?)null,
                episodeNumbers.Count > 0 ? episodeNumbers[0].Episode : (int?)null,
                matchQuery.KindHint,
                ct);
            return;
        }

        // Resolve via matcher (handles precedence chain + cache + error tolerance)
        var tmdbResult = await tmdbMatcher.ResolveAsync(matchQuery, ct);

        if (tmdbResult.NeedsReview)
        {
            // Determine the effective review reason:
            // If the NFO was malformed AND the TMDB lookup also failed, surface NfoMalformed reason.
            // Otherwise use the standard TMDB review reason.
            var effectiveReason = nfoResult is { ParsedSuccessfully: false }
                ? ReviewReason.NfoMalformed
                : tmdbResult.ReviewReason ?? ReviewReason.NoTmdbResult;

            await CreateReviewItemAsync(
                scanRun, root.Id, file, tmdbResult, matchQuery, episodeNumbers, counters,
                existingOpenReviewPaths, effectiveReason, ct);
            return;
        }

        // Successful TMDB match.
        // If the NFO was malformed but the filename fallback matched, emit a warning decision row
        // (NfoMalformed reason) alongside the standard Added decision so the report is transparent.
        if (nfoResult is { ParsedSuccessfully: false })
            await db.ScanItemDecisions.AddAsync(new ScanItemDecision
            {
                ScanRunId = scanRun.Id,
                FilePath = file.AbsolutePath,
                Kind = ScanDecisionKind.Added,
                Reason = ReviewReason.NfoMalformed.ToString(),
                LibraryRootId = root.Id,
                ParsedTitle = matchQuery.Title,
                ParsedYear = matchQuery.Year,
                ParsedMediaType = matchQuery.KindHint,
                CandidatesJson = SerializeCandidates(tmdbResult.Candidates)
            }, ct);

        inFlightPaths.Add(file.AbsolutePath);
        await PersistNewMediaFileAsync(
            scanRun, root, file, role, fingerprint, counters,
            tmdbResult.TmdbId, tmdbResult.Kind,
            SerializeCandidates(tmdbResult.Candidates),
            matchQuery.Title, matchQuery.Year,
            episodeNumbers.Count > 0 ? episodeNumbers[0].Season : (int?)null,
            episodeNumbers.Count > 0 ? episodeNumbers[0].Episode : (int?)null,
            matchQuery.KindHint,
            ct);
    }

    // =========================================================================
    // Persist NfoMetadata row (upsert by SourcePath)
    // =========================================================================

    private async Task PersistNfoMetadataAsync(string nfoPath, NfoParseResult result, CancellationToken ct)
    {
        // Check for an existing row to handle incremental rescans (unique index on SourcePath).
        var existing = await db.NfoMetadata
            .FirstOrDefaultAsync(n => n.SourcePath == nfoPath, ct);

        if (existing is not null)
        {
            // Update the existing row with freshly parsed values
            existing.Title = result.Title;
            existing.Year = result.Year;
            existing.TmdbId = result.TmdbId;
            existing.ImdbId = result.ImdbId;
            existing.Season = result.Season;
            existing.Episode = result.Episode;
            existing.ParseFailed = false;
            existing.ParseError = null;
            // RawContent not updated here — would require re-reading the file content;
            // the SourcePath unique index prevents stale state for most incremental scans.
        }
        else
        {
            await db.NfoMetadata.AddAsync(new NfoMetadata
            {
                SourcePath = nfoPath,
                // Store a brief summary in RawContent (actual parsed fields are stored in columns)
                RawContent = $"parsed:{result.Title}/{result.Year}/{result.TmdbId}",
                Title = result.Title,
                Year = result.Year,
                TmdbId = result.TmdbId,
                ImdbId = result.ImdbId,
                Season = result.Season,
                Episode = result.Episode,
                ParseFailed = false,
                ParseError = null
            }, ct);
        }
    }

    // =========================================================================
    // Persist a new MediaFile row for a successfully matched file
    // =========================================================================

    private async Task PersistNewMediaFileAsync(
        ScanRun scanRun,
        LibraryRoot root,
        NasFileEntry file,
        MediaFileRole role,
        string fingerprint,
        ScanCounters counters,
        int? assignedTmdbId,
        MediaType? assignedTmdbKind,
        string candidatesJson,
        string? parsedTitle,
        int? parsedYear,
        int? parsedSeason,
        int? parsedEpisode,
        MediaType? parsedMediaType,
        CancellationToken ct)
    {
        var mediaFile = new MediaFile
        {
            FilePath = file.AbsolutePath,
            Fingerprint = fingerprint,
            MtimeUtc = file.MtimeUtc,
            FileSizeBytes = file.SizeBytes,
            Format = file.Extension?.ToUpperInvariant(),
            LibraryRootId = root.Id,
            FirstSeenScanRunId = scanRun.Id,
            LastSeenScanRunId = scanRun.Id,
            Role = role
        };

        db.MediaFiles.Add(mediaFile);
        // Defer the save — the outer loop calls SaveChangesAsync once per root batch

        counters.Added++;
        var decision = new ScanItemDecision
        {
            ScanRunId = scanRun.Id,
            FilePath = file.AbsolutePath,
            Kind = ScanDecisionKind.Added,
            // Set navigation property explicitly so EF Core orders the inserts correctly
            // when saving a large batch (MediaFile must be inserted before ScanItemDecision)
            MediaFile = mediaFile,
            // New dashboard fields
            LibraryRootId = root.Id,
            AssignedTmdbId = assignedTmdbId,
            AssignedTmdbKind = assignedTmdbKind,
            CandidatesJson = candidatesJson,
            ParsedTitle = parsedTitle,
            ParsedYear = parsedYear,
            ParsedSeason = parsedSeason,
            ParsedEpisode = parsedEpisode,
            ParsedMediaType = parsedMediaType
        };
        await db.ScanItemDecisions.AddAsync(decision, ct);

        // Structured per-file decision log at Information level
        logger.LogInformation(
            "Scan decision: {ScanRunId} | {FilePath} | Kind={Kind}",
            scanRun.Id, file.AbsolutePath, ScanDecisionKind.Added);
    }

    // =========================================================================
    // Create a ReviewItem for an ambiguous / unmatched file
    // =========================================================================

    private async Task CreateReviewItemAsync(
        ScanRun scanRun,
        Guid libraryRootId,
        NasFileEntry file,
        TmdbMatchResult tmdbResult,
        MatchQuery matchQuery,
        IReadOnlyList<EpisodeNumber> episodeNumbers,
        ScanCounters counters,
        HashSet<string> existingOpenReviewPaths,
        ReviewReason effectiveReason,
        CancellationToken ct)
    {
        // Avoid creating duplicate Open ReviewItems for the same path (use pre-loaded set)
        if (!existingOpenReviewPaths.Contains(file.AbsolutePath))
        {
            var candidatesJson = JsonSerializer.Serialize(
                tmdbResult.Candidates.Select(c => new
                {
                    tmdbId = c.TmdbId,
                    kind = c.Kind.ToString(),
                    title = c.Title,
                    year = c.Year,
                    score = c.Score,
                    posterPath = c.PosterPath
                }));

            var reviewItem = new ReviewItem
            {
                FilePath = file.AbsolutePath,
                Reason = effectiveReason,
                Status = ReviewStatus.Open,
                ParsedTitle = matchQuery.Title,
                ParsedYear = matchQuery.Year,
                ParsedSeason = episodeNumbers.Count > 0 ? episodeNumbers[0].Season : null,
                ParsedEpisode = episodeNumbers.Count > 0 ? episodeNumbers[0].Episode : null,
                CandidatesJson = candidatesJson,
                FirstSeenScanRunId = scanRun.Id
            };

            db.ReviewItems.Add(reviewItem);
            existingOpenReviewPaths.Add(file.AbsolutePath); // prevent future duplicates in same batch
        }

        counters.NeedsReview++;
        await db.ScanItemDecisions.AddAsync(new ScanItemDecision
        {
            ScanRunId = scanRun.Id,
            FilePath = file.AbsolutePath,
            Kind = ScanDecisionKind.NeedsReview,
            Reason = effectiveReason.ToString(),
            LibraryRootId = libraryRootId,
            ParsedTitle = matchQuery.Title,
            ParsedYear = matchQuery.Year,
            ParsedSeason = episodeNumbers.Count > 0 ? episodeNumbers[0].Season : null,
            ParsedEpisode = episodeNumbers.Count > 0 ? episodeNumbers[0].Episode : null,
            ParsedMediaType = matchQuery.KindHint,
            CandidatesJson = SerializeCandidates(tmdbResult.Candidates)
        }, ct);

        // Structured per-file decision log for review items
        logger.LogInformation(
            "Scan decision: {ScanRunId} | {FilePath} | Kind={Kind} | Reason={Reason}",
            scanRun.Id, file.AbsolutePath, ScanDecisionKind.NeedsReview, effectiveReason);
    }

    // =========================================================================
    // Build a MatchQuery from file path, parsed episode data, and NFO result
    // =========================================================================

    private MatchQuery BuildMatchQuery(
        NasFileEntry file,
        LibraryRoot root,
        IReadOnlyList<EpisodeNumber> episodeNumbers,
        MediaFileRole role,
        NfoParseResult? nfoResult = null,
        string? language = null)
    {
        // Check for explicit TMDB id token in the file or folder name
        int? explicitTokenId = null;
        var tokenMatch = KodiRegexCatalog.ExplicitTmdbIdToken.Match(file.AbsolutePath);
        if (tokenMatch.Success && int.TryParse(tokenMatch.Groups[1].Value, out var parsedId))
            explicitTokenId = parsedId;

        // NFO TmdbId feeds the highest-precedence slot in the resolution chain:
        //   NfoTmdbId → ExplicitTokenId → Title+Year → Title
        // Only use TmdbId from well-formed NFO results.
        var nfoTmdbId = nfoResult?.ParsedSuccessfully == true ? nfoResult.TmdbId : null;

        // Parse the title and year from the filename using the Kodi name parser
        var kindHint = role == MediaFileRole.Episode || root.Kind == LibraryRootKind.TvShows
            ? MediaType.TvShow
            : MediaType.Film;

        if (kindHint == MediaType.Film)
        {
            var movieResult = nameParser.ParseMovie(file.AbsolutePath);

            // NFO fields override filename-parsed fields when the NFO is well-formed
            var title = nfoResult?.ParsedSuccessfully == true && nfoResult.Title is not null
                ? nfoResult.Title
                : movieResult.Title ?? Path.GetFileNameWithoutExtension(file.FileName);

            var year = nfoResult?.ParsedSuccessfully == true && nfoResult.Year is not null
                ? nfoResult.Year
                : movieResult.Year;

            return new MatchQuery(
                title,
                year,
                MediaType.Film,
                nfoTmdbId,
                explicitTokenId,
                Language: language ?? "en-US");
        }
        else
        {
            var episodeResult = nameParser.ParseEpisode(file.AbsolutePath, BuildEpisodeHint(file.AbsolutePath));

            var title = nfoResult?.ParsedSuccessfully == true && nfoResult.Title is not null
                ? nfoResult.Title
                : episodeResult.Title ?? Path.GetFileNameWithoutExtension(file.FileName);

            // F2 guard: only set FallbackTitle when it differs from the filename-derived title
            // to avoid a duplicate TMDB call with no benefit.
            var parsedTitle = episodeResult.Title;
            var folderTitle = episodeResult.FolderTitle;
            var fallbackTitle = folderTitle != null && folderTitle != parsedTitle ? folderTitle : null;

            // SearchLanguages priority: LibraryRoot > Scanner:DefaultSearchLanguages config > null (uses "en-US" default)
            var searchLanguages = root.SearchLanguages is { Count: > 0 }
                ? root.SearchLanguages
                : configuration?.GetSection("Scanner:DefaultSearchLanguages").Get<string[]>() is { Length: > 0 } cfg
                    ? (IReadOnlyList<string>)cfg
                    : null;

            return new MatchQuery(
                title,
                null,
                MediaType.TvShow,
                nfoTmdbId,
                explicitTokenId,
                Language: language ?? "en-US",
                FallbackTitle: fallbackTitle,
                SearchLanguages: searchLanguages);
        }
    }

    // =========================================================================
    // Removed-file detection
    // SOURCE: quickstart.md §6 — files missing from NAS get MissingSince set
    // =========================================================================

    private async Task MarkRemovedFilesAsync(
        ScanRun scanRun,
        IReadOnlyList<LibraryRoot> roots,
        ScanCounters counters,
        CancellationToken ct)
    {
        var rootIds = roots.Select(r => r.Id).ToHashSet();

        // Find MediaFiles belonging to these roots that were NOT seen in this scan
        var missingFiles = await db.MediaFiles
            .Where(mf =>
                mf.LibraryRootId.HasValue
                && rootIds.Contains(mf.LibraryRootId!.Value)
                && mf.LastSeenScanRunId != scanRun.Id
                && mf.MissingSince == null)
            .ToListAsync(ct);

        foreach (var mf in missingFiles)
        {
            mf.MissingSince = DateTime.UtcNow;
            counters.Removed++;
            await db.ScanItemDecisions.AddAsync(new ScanItemDecision
            {
                ScanRunId = scanRun.Id,
                FilePath = mf.FilePath,
                Kind = ScanDecisionKind.Removed,
                MediaFileId = mf.Id,
                LibraryRootId = mf.LibraryRootId
            }, ct);

            logger.LogInformation(
                "Scan decision: {ScanRunId} | {FilePath} | Kind={Kind}",
                scanRun.Id, mf.FilePath, ScanDecisionKind.Removed);
        }
    }

    // =========================================================================
    // Fingerprint: SHA-256 of "absPath|size|mtimeUnix"
    // =========================================================================

    internal static string ComputeFingerprint(string absPath, long sizeBytes, DateTime mtimeUtc)
    {
        var raw = $"{absPath}|{sizeBytes}|{((DateTimeOffset)mtimeUtc).ToUnixTimeSeconds()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static EpisodeNumberingHint BuildEpisodeHint(string fullPath)
    {
        // SOURCE: Kodi wiki — Season folder provides context for ambiguous filenames
        var normalised = fullPath.Replace('\\', '/');
        var segments = normalised.Split('/');
        foreach (var segment in segments)
        {
            var m = KodiRegexCatalog.SeasonFolderName.Match(segment);
            if (m.Success)
            {
                var seasonStr = m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : m.Groups[2].Value;
                if (int.TryParse(seasonStr, out var seasonNum))
                    return new EpisodeNumberingHint(seasonNum);
            }

            if (KodiRegexCatalog.SpecialsFolderName.IsMatch(segment))
                return new EpisodeNumberingHint(0);
        }

        return new EpisodeNumberingHint();
    }

    private static async Task EmitProgressAsync(
        Guid scanRunId, string phase, int processed, int total,
        string? lastPath, ChannelWriter<ScanProgressDto> writer, CancellationToken ct)
    {
        try
        {
            await writer.WriteAsync(
                new ScanProgressDto(scanRunId, phase, processed, total, lastPath, null), ct);
        }
        catch (ChannelClosedException)
        {
            /* subscriber gone */
        }
    }

    // =========================================================================
    // Serialize TMDB candidates for ScanItemDecision.CandidatesJson
    // =========================================================================

    private static string SerializeCandidates(IReadOnlyList<TmdbCandidate> candidates)
    {
        if (candidates.Count == 0) return "[]";
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
    // Inner counter struct
    // =========================================================================

    private sealed class ScanCounters
    {
        public int Added;
        public int Excluded;
        public int NeedsReview;
        public int Removed;
        public int TotalDiscovered;
        public int Unchanged;
        public int Updated;
    }
}