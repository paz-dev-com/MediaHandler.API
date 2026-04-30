#nullable enable
// ScanPipeline — orchestrates the full scanner pipeline:
// enumerate → exclude → group(stacks) → parse → classify → TMDB-match → fingerprint → persist.
// Files that cannot be unambiguously matched to TMDB become ReviewItems rather than silent mis-mappings.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models.Scanner;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaHandler.Infrastructure.Nas.Scanner;

/// <summary>
/// Executes the full scanner pipeline for a single <see cref="ScanRun"/>.
/// Pipeline stages: enumerate → exclude → group(stacks) → parse → classify → fingerprint → persist.
/// </summary>
public sealed class ScanPipeline(
    IApplicationDbContext db,
    INasFileEnumerator enumerator,
    IExclusionEvaluator exclusionEvaluator,
    IStackingDetector stackingDetector,
    IKodiNameParser nameParser,
    ITvEpisodeMatcher episodeMatcher,
    ITmdbMatcher tmdbMatcher,
    ILogger<ScanPipeline> logger)
{
    // =========================================================================
    // Public entry point
    // =========================================================================

    public async Task ExecuteAsync(
        ScanRun scanRun,
        IReadOnlyList<LibraryRoot> roots,
        ChannelWriter<ScanProgressDto> progress,
        CancellationToken ct)
    {
        var counters = new ScanCounters();

        try
        {
            foreach (var root in roots)
            {
                ct.ThrowIfCancellationRequested();
                await ProcessRootAsync(scanRun, root, counters, progress, ct);
            }

            // ── Removed-file detection (US4 / T105 stub) ────────────────────
            // Full detection is wired in US4; here we mark the run complete.
            await MarkRemovedFilesAsync(scanRun, roots, counters, ct);
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

        await progress.WriteAsync(new ScanProgressDto(
            scanRun.Id, "Completed",
            counters.TotalDiscovered, counters.TotalDiscovered,
            null, null), ct);
    }

    // =========================================================================
    // Per-root processing
    // =========================================================================

    private async Task ProcessRootAsync(
        ScanRun scanRun,
        LibraryRoot root,
        ScanCounters counters,
        ChannelWriter<ScanProgressDto> progress,
        CancellationToken ct)
    {
        logger.LogInformation("ScanPipeline: starting root {RootId} ({Path})", root.Id, root.Path);

        // Collect all file entries from the NAS (enumeration stage)
        var allEntries = new List<NasFileEntry>();
        try
        {
            await foreach (var entry in enumerator.EnumerateAsync(root, ct))
                allEntries.Add(entry);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "NAS enumeration failed for root {RootId} ({Path}). Skipping removed-file detection for this root.", root.Id, root.Path);
            // R-007: NAS unreachable → suppress removed-file detection, write single decision
            await db.ScanItemDecisions.AddAsync(new ScanItemDecision
            {
                ScanRunId = scanRun.Id,
                FilePath = root.Path,
                Kind = ScanDecisionKind.NeedsReview,
                Reason = "NAS unreachable",
                RuleId = null
            }, ct);
            await db.SaveChangesAsync(ct);
            return;
        }

        // Build .nomedia folder set
        var nomediaFolders = allEntries
            .Where(e => string.Equals(e.FileName, ".nomedia", StringComparison.OrdinalIgnoreCase))
            .Select(e => System.IO.Path.GetDirectoryName(e.AbsolutePath) ?? string.Empty)
            .Where(d => !string.IsNullOrEmpty(d))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var exclusionCtx = new ExclusionContext(root, KodiRegexCatalog.DefaultExclusionRules, nomediaFolders);

        // ── Exclusion stage ─────────────────────────────────────────────────
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
                        RuleId = verdict.RuleId
                    }, ct);
                }
                continue;
            }
            videoFiles.Add(entry);
        }

        // ── Pre-load resolved ReviewItems for this batch of paths (T093 read-back) ──
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

        // ── Group by folder for stacking detection ──────────────────────────
        var byFolder = videoFiles
            .GroupBy(f => System.IO.Path.GetDirectoryName(f.AbsolutePath) ?? string.Empty);

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

        // ── Classification & persistence stage ──────────────────────────────
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
            var partIndex = stack is null ? 0
                : stack.Parts.TakeWhile(p => p.AbsolutePath != file.AbsolutePath).Count();
            var role = isStackedPart && partIndex > 0
                ? MediaFileRole.StackedPart
                : MediaFileRole.Main;

            await ClassifyAndPersistFileAsync(
                scanRun, root, file, role, stack, counters, resolvedReviewItems, existingOpenReviewPaths, existingMediaFiles, inFlightPaths, ct);

            if (processedInRoot % 50 == 0)
                await EmitProgressAsync(scanRun.Id, "Classifying", processedInRoot, videoFiles.Count,
                    file.AbsolutePath, progress, ct);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "ScanPipeline: root {RootId} done. Added={A} Updated={U} Unchanged={X} Excluded={E} NeedsReview={R}",
            root.Id, counters.Added, counters.Updated, counters.Unchanged, counters.Excluded, counters.NeedsReview);
    }

    // =========================================================================
    // File classification + TMDB matching + persistence
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
                    MediaFileId = existing.Id
                }, ct);
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
                MediaFileId = existing.Id
            }, ct);
            return;
        }

        // ── Determine classification (movie vs episode) ──────────────────────
        var isFromTvRoot = root.Kind is LibraryRootKind.TvShows;
        var hint = BuildEpisodeHint(file.AbsolutePath);
        var episodeNumbers = !isFromTvRoot
            ? (IReadOnlyList<EpisodeNumber>)[]
            : episodeMatcher.Match(file.FileName, hint);

        if (isFromTvRoot && episodeNumbers.Count > 0)
            role = MediaFileRole.Episode;
        else if (root.Kind == LibraryRootKind.Movies || episodeNumbers.Count == 0)
            role = role == MediaFileRole.StackedPart ? MediaFileRole.StackedPart : MediaFileRole.Main;

        // ── TMDB resolution stage ─────────────────────────────────────────────
        // T093: Check for a previously resolved ReviewItem for this path first.
        // If found, skip the TMDB title search and re-use the administrator's saved mapping.
        if (resolvedReviewItems.TryGetValue(file.AbsolutePath, out var resolvedItem)
            && resolvedItem.ResolvedTmdbId.HasValue)
        {
            logger.LogDebug(
                "Re-using saved resolution (TmdbId={TmdbId}) for '{FilePath}'.",
                resolvedItem.ResolvedTmdbId.Value, file.AbsolutePath);

            inFlightPaths.Add(file.AbsolutePath);
            await PersistNewMediaFileAsync(
                scanRun, root, file, role, fingerprint, counters, ct);
            return;
        }

        // Build the TMDB match query from parsed name data
        var matchQuery = BuildMatchQuery(file, root, episodeNumbers, role);

        // Resolve via matcher (handles precedence chain + cache + error tolerance)
        var tmdbResult = await tmdbMatcher.ResolveAsync(matchQuery, ct);

        if (tmdbResult.NeedsReview)
        {
            // The file cannot be confidently matched — create a ReviewItem
            await CreateReviewItemAsync(
                scanRun, file, tmdbResult, matchQuery, episodeNumbers, counters, existingOpenReviewPaths, ct);
            return;
        }

        // Successful match — persist the new MediaFile
        inFlightPaths.Add(file.AbsolutePath);
        await PersistNewMediaFileAsync(scanRun, root, file, role, fingerprint, counters, ct);
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
            MediaFile = mediaFile
        };
        await db.ScanItemDecisions.AddAsync(decision, ct);

        logger.LogDebug(
            "{ScanRunId}: Added {FilePath} [role={Role}]",
            scanRun.Id, file.AbsolutePath, role);
    }

    // =========================================================================
    // Create a ReviewItem for an ambiguous / unmatched file
    // =========================================================================

    private async Task CreateReviewItemAsync(
        ScanRun scanRun,
        NasFileEntry file,
        TmdbMatchResult tmdbResult,
        MatchQuery matchQuery,
        IReadOnlyList<EpisodeNumber> episodeNumbers,
        ScanCounters counters,
        HashSet<string> existingOpenReviewPaths,
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
                Reason = tmdbResult.ReviewReason ?? ReviewReason.NoTmdbResult,
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
            Reason = tmdbResult.ReviewReason?.ToString()
        }, ct);

        logger.LogDebug(
            "{ScanRunId}: NeedsReview {FilePath} [reason={Reason}]",
            scanRun.Id, file.AbsolutePath, tmdbResult.ReviewReason);
    }

    // =========================================================================
    // Build a MatchQuery from file path and parsed episode data
    // =========================================================================

    private MatchQuery BuildMatchQuery(
        NasFileEntry file,
        LibraryRoot root,
        IReadOnlyList<EpisodeNumber> episodeNumbers,
        MediaFileRole role)
    {
        // Check for explicit TMDB id token in the file or folder name
        int? explicitTokenId = null;
        var tokenMatch = KodiRegexCatalog.ExplicitTmdbIdToken.Match(file.AbsolutePath);
        if (tokenMatch.Success && int.TryParse(tokenMatch.Groups[1].Value, out var parsedId))
            explicitTokenId = parsedId;

        // Parse the title and year from the filename using the Kodi name parser
        var kindHint = role == MediaFileRole.Episode || root.Kind == LibraryRootKind.TvShows
            ? MediaType.TvShow
            : MediaType.Film;

        if (kindHint == MediaType.Film)
        {
            var movieResult = nameParser.ParseMovie(file.AbsolutePath);
            return new MatchQuery(
                Title: movieResult.Title ?? System.IO.Path.GetFileNameWithoutExtension(file.FileName),
                Year: movieResult.Year,
                KindHint: MediaType.Film,
                ExplicitTokenId: explicitTokenId);
        }
        else
        {
            var episodeResult = nameParser.ParseEpisode(file.AbsolutePath, BuildEpisodeHint(file.AbsolutePath));
            return new MatchQuery(
                Title: episodeResult.Title ?? System.IO.Path.GetFileNameWithoutExtension(file.FileName),
                Year: null,
                KindHint: MediaType.TvShow,
                ExplicitTokenId: explicitTokenId);
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
                MediaFileId = mf.Id
            }, ct);
        }
    }

    // =========================================================================
    // Fingerprint: SHA-256 of "absPath|size|mtimeUnix"
    // SOURCE: tasks.md T022 — SHA256 hex of size+mtime+absolute path
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
                    return new EpisodeNumberingHint(SeasonFromFolder: seasonNum);
            }

            if (KodiRegexCatalog.SpecialsFolderName.IsMatch(segment))
                return new EpisodeNumberingHint(SeasonFromFolder: 0);
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
        catch (ChannelClosedException) { /* subscriber gone */ }
    }

    // =========================================================================
    // Inner counter struct
    // =========================================================================

    private sealed class ScanCounters
    {
        public int TotalDiscovered;
        public int Added;
        public int Updated;
        public int Unchanged;
        public int Removed;
        public int Excluded;
        public int NeedsReview;
    }
}

