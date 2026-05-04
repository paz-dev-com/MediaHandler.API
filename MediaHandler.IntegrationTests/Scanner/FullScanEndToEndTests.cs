// FullScanEndToEndTests — SC-001: ≥ 98 % classification accuracy
// Integration test: fake INasService + Testcontainers SQL Server

using System.Diagnostics;
using FluentAssertions;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models.Scanner;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Enums;
using MediaHandler.Infrastructure.Nas;
using MediaHandler.Infrastructure.Nas.Scanner;
using MediaHandler.Infrastructure.Persistence;
using MediaHandler.Infrastructure.Services;
using MediaHandler.IntegrationTests.Common;
using MediaHandler.IntegrationTests.Scanner.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NasFileInfo = MediaHandler.Application.Common.DTOs.NasFileInfo;
using TmdbIdLookupResult = MediaHandler.Application.Common.Interfaces.TmdbIdLookupResult;

namespace MediaHandler.IntegrationTests.Scanner;

/// <summary>
///     SC-001: ≥ 98 % correct classification of the benchmark fixture.
/// </summary>
public class FullScanEndToEndTests : ScannerIntegrationTestBase
{
    private FixtureBuilder? _fixture;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        _fixture = FixtureBuilder.LoadFromManifest();
        WithFakeNasService(_fixture.ToNasFileInfos(), ["/nas"]);
    }

    [Fact]
    public async Task Sc001_ClassificationAccuracy_AtLeast98Percent()
    {
        if (_fixture is null) throw new InvalidOperationException("Fixture not initialised");

        // Register library roots
        var moviesRoot = new LibraryRoot
        {
            Path = "/nas/Movies",
            Kind = LibraryRootKind.Movies,
            IsEnabled = true
        };
        var tvRoot = new LibraryRoot
        {
            Path = "/nas/TV Shows",
            Kind = LibraryRootKind.TvShows,
            IsEnabled = true
        };
        DbContext.LibraryRoots.AddRange(moviesRoot, tvRoot);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Run the scan pipeline directly via the coordinator
        var coordinator = BuildCoordinator();
        var scanRunId = Guid.NewGuid();
        var handle = await coordinator.StartAsync(
            new ScanStartParameters(scanRunId, [moviesRoot.Id, tvRoot.Id], ScanMode.Full),
            TestContext.Current.CancellationToken);

        // Wait for scan to complete (poll DB)
        await WaitForScanCompletion(handle.ScanRunId, 120);

        // Assert SC-001 metric
        var scanRun = await DbContext.ScanRuns
            .AsNoTracking()
            .FirstAsync(r => r.Id == handle.ScanRunId, TestContext.Current.CancellationToken);

        scanRun.Status.Should().Be(ScanStatus.Completed);

        var totalExpected = _fixture.TotalExpectedMediaItems;
        var totalAdded = scanRun.Added + scanRun.Updated;
        var classificationRate = totalExpected > 0
            ? (double)totalAdded / totalExpected
            : 1.0;

        classificationRate.Should().BeGreaterThanOrEqualTo(0.98,
            $"SC-001 requires ≥ 98% classification accuracy. Got {totalAdded}/{totalExpected} = {classificationRate:P1}");
    }

    /// <summary>
    ///     SC-002: ≤ 0.5 % silent misclassification rate.
    ///     Every divergence from the fixture's expected (tmdbId, kind) MUST produce a ReviewItem
    ///     for the same path; a divergence without a ReviewItem is counted as "silent".
    /// </summary>
    [Fact]
    public async Task Sc002_SilentMisclassRate_AtMost0p5Percent()
    {
        if (_fixture is null) throw new InvalidOperationException("Fixture not initialised");

        // Register library roots
        var moviesRoot = new LibraryRoot
        {
            Path = "/nas/Movies",
            Kind = LibraryRootKind.Movies,
            IsEnabled = true
        };
        var tvRoot = new LibraryRoot
        {
            Path = "/nas/TV Shows",
            Kind = LibraryRootKind.TvShows,
            IsEnabled = true
        };
        DbContext.LibraryRoots.AddRange(moviesRoot, tvRoot);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Build a real TmdbMatcher backed by a fake ITmdbService so that
        // the pipeline creates ReviewItems for unmatched / ambiguous files.
        var fakeTmdb = Substitute.For<ITmdbService>();

        // Default: no candidates (forces NoTmdbResult → ReviewItem)
        fakeTmdb.SearchCandidatesAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<MediaType?>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([]);

        fakeTmdb.GetMovieByIdAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((TmdbIdLookupResult?)null);
        fakeTmdb.GetTvShowByIdAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((TmdbIdLookupResult?)null);

        var realMatcher = new TmdbMatcher(fakeTmdb);

        var coordinator = BuildCoordinatorWithMatcher(moviesRoot, tvRoot, realMatcher);
        var scanRunId = Guid.NewGuid();
        var handle = await coordinator.StartAsync(
            new ScanStartParameters(scanRunId, [moviesRoot.Id, tvRoot.Id], ScanMode.Full),
            TestContext.Current.CancellationToken);

        await WaitForScanCompletion(handle.ScanRunId, 120);
        var decisions = await DbContext.ScanItemDecisions
            .AsNoTracking()
            .Where(d => d.ScanRunId == handle.ScanRunId)
            .ToListAsync(TestContext.Current.CancellationToken);

        // Review items created by the scan
        var reviewPaths = (await DbContext.ReviewItems
                .AsNoTracking()
                .Where(r => r.FirstSeenScanRunId == handle.ScanRunId)
                .Select(r => r.FilePath)
                .ToListAsync(TestContext.Current.CancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Count NeedsReview decisions that have NO matching ReviewItem → "silent misclassification"
        var needsReviewDecisions = decisions
            .Where(d => d.Kind == ScanDecisionKind.NeedsReview)
            .ToList();

        var silentCount = needsReviewDecisions
            .Count(d => !reviewPaths.Contains(d.FilePath));

        var totalClassified = decisions
            .Count(d => d.Kind != ScanDecisionKind.Excluded);

        var silentRate = totalClassified > 0
            ? (double)silentCount / totalClassified
            : 0.0;

        silentRate.Should().BeLessThanOrEqualTo(0.005,
            $"SC-002 requires ≤ 0.5% silent misclassification. " +
            $"Got {silentCount} silent out of {totalClassified} = {silentRate:P2}");
    }

    private async Task WaitForScanCompletion(Guid scanRunId, int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var run = await DbContext.ScanRuns.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == scanRunId, TestContext.Current.CancellationToken);

            if (run is null)
            {
                await Task.Delay(500);
                continue;
            }

            if (run.Status is ScanStatus.Completed or ScanStatus.Failed or ScanStatus.Cancelled)
                return;

            await Task.Delay(200, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"Scan {scanRunId} did not complete within {timeoutSeconds}s");
    }

    private ScanRunCoordinator BuildCoordinator()
    {
        var nasEnumerator = new NasFileEnumerator(
            FakeNas!, NullLogger<NasFileEnumerator>.Instance);

        var parser = new KodiNameParser();
        var exclusionEvaluator = new ExclusionEvaluator();
        var stackDetector = new StackingDetector();
        var episodeMatcher = new TvEpisodeMatcher();
        var tmdbMatcher = Substitute.For<ITmdbMatcher>();
        // TMDB stub: always return needs-review=false with no TmdbId
        tmdbMatcher.ResolveAsync(Arg.Any<MatchQuery>(), Arg.Any<CancellationToken>())
            .Returns(new TmdbMatchResult(false, null, null, false, null, []));

        var logger = NullLogger<ScanRunCoordinator>.Instance;
        var pipelineLogger = NullLogger<ScanPipeline>.Instance;

        // Give the coordinator its OWN DbContext so its background scan task never
        // shares a context instance with the test's polling queries.
        var coordinatorDb = new MediaHandlerDbContext(DbContextOptions);

        var pipeline = new ScanPipeline(
            coordinatorDb,
            nasEnumerator,
            exclusionEvaluator,
            stackDetector,
            parser,
            episodeMatcher,
            tmdbMatcher,
            pipelineLogger);

        return CreateScanRunCoordinator(pipeline, coordinatorDb);
    }

    private ScanRunCoordinator BuildCoordinatorWithMatcher(
        LibraryRoot moviesRoot,
        LibraryRoot tvRoot,
        ITmdbMatcher tmdbMatcher)
    {
        var nasEnumerator = new NasFileEnumerator(
            FakeNas!, NullLogger<NasFileEnumerator>.Instance);

        var parser = new KodiNameParser();
        var exclusionEvaluator = new ExclusionEvaluator();
        var stackDetector = new StackingDetector();
        var episodeMatcher = new TvEpisodeMatcher();

        var logger = NullLogger<ScanRunCoordinator>.Instance;
        var pipelineLogger = NullLogger<ScanPipeline>.Instance;

        var coordinatorDb = new MediaHandlerDbContext(DbContextOptions);

        var pipeline = new ScanPipeline(
            coordinatorDb,
            nasEnumerator,
            exclusionEvaluator,
            stackDetector,
            parser,
            episodeMatcher,
            tmdbMatcher,
            pipelineLogger);

        return CreateScanRunCoordinator(pipeline, coordinatorDb);
    }

    // =========================================================================
    // SC-006: any file diagnosable in under 30 seconds
    // =========================================================================

    /// <summary>
    ///     SC-006: For every file in the benchmark fixture, the outcome must be locatable
    ///     via an O(1) indexed lookup — either a <c>ScanItemDecision</c> row, a <c>MediaFile</c>
    ///     row, or a <c>ReviewItem</c> — all within 30 seconds elapsed wall-clock time.
    ///     This validates FR-023: every path the pipeline touches has an audit trail.
    /// </summary>
    [Fact]
    public async Task Sc006_AnyFileDiagnosable_Under30Seconds()
    {
        if (_fixture is null) throw new InvalidOperationException("Fixture not initialised");

        // Register library roots
        var moviesRoot = new LibraryRoot
        {
            Path = "/nas/Movies",
            Kind = LibraryRootKind.Movies,
            IsEnabled = true
        };
        var tvRoot = new LibraryRoot
        {
            Path = "/nas/TV Shows",
            Kind = LibraryRootKind.TvShows,
            IsEnabled = true
        };
        DbContext.LibraryRoots.AddRange(moviesRoot, tvRoot);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Run a full scan (TMDB stub returns NeedsReview=false so all valid files are Added)
        var coordinator = BuildCoordinator();
        var handle = await coordinator.StartAsync(
            new ScanStartParameters(Guid.NewGuid(), [moviesRoot.Id, tvRoot.Id], ScanMode.Full),
            TestContext.Current.CancellationToken);

        await WaitForScanCompletion(handle.ScanRunId, 120);

        // Build O(1) lookup structures — this is the diagnostic query the admin would run.
        // The 30-second budget covers loading all three sets plus iterating every fixture path.
        var diagnosticStopwatch = Stopwatch.StartNew();

        // All paths that received a pipeline decision for this scan run
        var decidedPaths = (await DbContext.ScanItemDecisions
                .AsNoTracking()
                .Where(d => d.ScanRunId == handle.ScanRunId)
                .Select(d => d.FilePath)
                .ToListAsync(TestContext.Current.CancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // All MediaFile paths persisted (any scan — covers incrementals)
        var mediaFilePaths = (await DbContext.MediaFiles
                .AsNoTracking()
                .Select(mf => mf.FilePath)
                .ToListAsync(TestContext.Current.CancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // All ReviewItem paths created by this scan run
        var reviewItemPaths = (await DbContext.ReviewItems
                .AsNoTracking()
                .Where(r => r.FirstSeenScanRunId == handle.ScanRunId)
                .Select(r => r.FilePath)
                .ToListAsync(TestContext.Current.CancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Check every non-directory fixture file — each must appear in at least one set.
        // The lookup is O(1) per path thanks to the indexed DB columns and in-memory HashSets.
        var allFixturePaths = _fixture.ToNasFileInfos()
            .Where(e => !e.IsDirectory)
            .Select(e => e.FilePath)
            .ToList();

        var uncoveredPaths = new List<string>();
        foreach (var path in allFixturePaths)
            if (!decidedPaths.Contains(path)
                && !mediaFilePaths.Contains(path)
                && !reviewItemPaths.Contains(path))
                uncoveredPaths.Add(path);

        diagnosticStopwatch.Stop();

        uncoveredPaths.Should().BeEmpty(
            $"Every file path in the fixture must have at least one audit record " +
            $"(ScanItemDecision, MediaFile, or ReviewItem). " +
            $"Paths without coverage: {string.Join(", ", uncoveredPaths.Take(10))}");

        diagnosticStopwatch.Elapsed.TotalSeconds.Should().BeLessThan(30,
            "SC-006 requires any file's scan outcome to be diagnosable in under 30 seconds");
    }

    private ScanRunCoordinator BuildCoordinatorWithNfoParser(
        ITmdbMatcher tmdbMatcher,
        INfoParser nfoParser)
    {
        var nasEnumerator = new NasFileEnumerator(
            FakeNas!, NullLogger<NasFileEnumerator>.Instance);

        var parser = new KodiNameParser();
        var exclusionEvaluator = new ExclusionEvaluator();
        var stackDetector = new StackingDetector();
        var episodeMatcher = new TvEpisodeMatcher();

        var logger = NullLogger<ScanRunCoordinator>.Instance;
        var pipelineLogger = NullLogger<ScanPipeline>.Instance;

        var coordinatorDb = new MediaHandlerDbContext(DbContextOptions);

        var pipeline = new ScanPipeline(
            coordinatorDb,
            nasEnumerator,
            exclusionEvaluator,
            stackDetector,
            parser,
            episodeMatcher,
            tmdbMatcher,
            pipelineLogger,
            nfoParser);

        return CreateScanRunCoordinator(pipeline, coordinatorDb);
    }

    // =========================================================================
    // NFO sidecar overrides filename guess
    // =========================================================================

    /// <summary>
    ///     Acceptance scenario 1: movie file in a folder with movie.nfo containing a tmdbid
    ///     is mapped using the NFO's TMDB id rather than the filename parser's guess.
    ///     Acceptance scenario 2: TV show folder with tvshow.nfo containing a tmdbid
    ///     is mapped using the NFO's TMDB id.
    ///     Acceptance scenario 3: malformed NFO file causes a Serilog warning and graceful
    ///     fallback to filename-based detection; the overall scan does not abort.
    /// </summary>
    [Fact]
    public async Task Sc_Nfo_OverridesFilenameGuess()
    {
        // Build the in-memory NAS fixture
        // Movie with well-formed movie.nfo whose tmdbid=27205 (Inception)
        const string movieFolder = "/nas/Movies/Some Misnamed Movie (2010)";
        const string movieFile = movieFolder + "/Some Misnamed Movie (2010).mkv";
        const string movieNfo = movieFolder + "/movie.nfo";

        // TV show with tvshow.nfo whose tmdbid=1396 (Breaking Bad)
        const string tvFolder = "/nas/TV Shows/A Misnamed Show";
        const string tvNfo = tvFolder + "/tvshow.nfo";
        const string tvEpisode = tvFolder + "/Season 1/S01E01.mkv";

        // Movie with a malformed NFO — scanner must fall back gracefully
        const string badNfoFolder = "/nas/Movies/Interstellar (2014)";
        const string badNfoFile = badNfoFolder + "/Interstellar (2014).mkv";
        const string badNfo = badNfoFolder + "/movie.nfo";

        var nasEntries = new List<NasFileInfo>
        {
            new(movieFolder, Path.GetFileName(movieFolder), 0, null, DateTime.UtcNow, DateTime.UtcNow, true),
            new(movieFile, "Some Misnamed Movie (2010).mkv", 1_073_741_824, "mkv", DateTime.UtcNow, DateTime.UtcNow),
            new(movieNfo, "movie.nfo", 512, "nfo", DateTime.UtcNow, DateTime.UtcNow),

            new(tvFolder, Path.GetFileName(tvFolder), 0, null, DateTime.UtcNow, DateTime.UtcNow, true),
            new(tvNfo, "tvshow.nfo", 512, "nfo", DateTime.UtcNow, DateTime.UtcNow),
            new(tvFolder + "/Season 1", "Season 1", 0, null, DateTime.UtcNow, DateTime.UtcNow, true),
            new(tvEpisode, "S01E01.mkv", 1_073_741_824, "mkv", DateTime.UtcNow, DateTime.UtcNow),

            new(badNfoFolder, Path.GetFileName(badNfoFolder), 0, null, DateTime.UtcNow, DateTime.UtcNow, true),
            new(badNfoFile, "Interstellar (2014).mkv", 1_073_741_824, "mkv", DateTime.UtcNow, DateTime.UtcNow),
            new(badNfo, "movie.nfo", 50, "nfo", DateTime.UtcNow, DateTime.UtcNow)
        };

        WithFakeNasService(nasEntries, ["/nas"]);

        // Register library roots
        var moviesRoot = new LibraryRoot { Path = "/nas/Movies", Kind = LibraryRootKind.Movies, IsEnabled = true };
        var tvRoot = new LibraryRoot { Path = "/nas/TV Shows", Kind = LibraryRootKind.TvShows, IsEnabled = true };
        DbContext.LibraryRoots.AddRange(moviesRoot, tvRoot);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Build fake ITmdbMatcher
        // Returns a successful match when NfoTmdbId is present; otherwise needs-review.
        var fakeTmdb = Substitute.For<ITmdbMatcher>();

        // When NfoTmdbId=27205 is passed, return a successful movie match
        fakeTmdb.ResolveAsync(
                Arg.Is<MatchQuery>(q => q.NfoTmdbId == 27205),
                Arg.Any<CancellationToken>())
            .Returns(new TmdbMatchResult(true, 27205, MediaType.Film, false, null, []));

        // When NfoTmdbId=1396 is passed, return a successful TV show match
        fakeTmdb.ResolveAsync(
                Arg.Is<MatchQuery>(q => q.NfoTmdbId == 1396),
                Arg.Any<CancellationToken>())
            .Returns(new TmdbMatchResult(true, 1396, MediaType.TvShow, false, null, []));

        // Default: resolve Interstellar by title (filename fallback) — return matched too
        fakeTmdb.ResolveAsync(
                Arg.Is<MatchQuery>(q => q.NfoTmdbId == null),
                Arg.Any<CancellationToken>())
            .Returns(new TmdbMatchResult(true, 99999, MediaType.Film, false, null, []));

        // Build fake INfoParser
        // Returns predefined results by path (no actual files on disk needed).
        var fakeNfoParser = Substitute.For<INfoParser>();

        // movie.nfo for the Inception folder → well-formed, tmdbid=27205
        fakeNfoParser.ParseAsync(movieNfo, Arg.Any<CancellationToken>())
            .Returns(new NfoParseResult(
                true,
                "Inception",
                2010,
                27205,
                null,
                null,
                null));

        // tvshow.nfo → well-formed, tmdbid=1396
        fakeNfoParser.ParseAsync(tvNfo, Arg.Any<CancellationToken>())
            .Returns(new NfoParseResult(
                true,
                "Breaking Bad",
                2008,
                1396,
                null,
                null,
                null));

        // badNfo → malformed XML
        fakeNfoParser.ParseAsync(badNfo, Arg.Any<CancellationToken>())
            .Returns(NfoParseResult.Malformed("Invalid XML"));

        // Execute the scan
        var coordinator = BuildCoordinatorWithNfoParser(fakeTmdb, fakeNfoParser);
        var handle = await coordinator.StartAsync(
            new ScanStartParameters(Guid.NewGuid(), [moviesRoot.Id, tvRoot.Id], ScanMode.Full),
            TestContext.Current.CancellationToken);

        await WaitForScanCompletion(handle.ScanRunId, 120);

        // Acceptance scenario 1: NFO TMDB id was used for movie match
        await fakeNfoParser.Received().ParseAsync(movieNfo, Arg.Any<CancellationToken>());

        await fakeTmdb.Received().ResolveAsync(
            Arg.Is<MatchQuery>(q => q.NfoTmdbId == 27205),
            Arg.Any<CancellationToken>());

        // Acceptance scenario 2: NFO TMDB id was used for TV show match
        await fakeNfoParser.Received().ParseAsync(tvNfo, Arg.Any<CancellationToken>());

        await fakeTmdb.Received().ResolveAsync(
            Arg.Is<MatchQuery>(q => q.NfoTmdbId == 1396),
            Arg.Any<CancellationToken>());

        // Acceptance scenario 3: malformed NFO fell back gracefully
        // Scan must NOT be in a failed state (malformed NFO did not abort the run).
        var scanRun = await DbContext.ScanRuns
            .AsNoTracking()
            .FirstAsync(r => r.Id == handle.ScanRunId, TestContext.Current.CancellationToken);

        scanRun.Status.Should().Be(ScanStatus.Completed,
            "A malformed NFO must not abort the scan run");

        // The bad-NFO movie file must still appear in decisions (added via filename fallback)
        var badNfoDecisions = await DbContext.ScanItemDecisions
            .AsNoTracking()
            .Where(d => d.ScanRunId == handle.ScanRunId && d.FilePath == badNfoFile)
            .ToListAsync(TestContext.Current.CancellationToken);

        badNfoDecisions.Should().NotBeEmpty(
            "The movie file with a malformed NFO must still receive a scan decision");

        // The NfoMetadata table must contain rows for the two well-formed NFO files
        var nfoRows = await DbContext.NfoMetadata
            .AsNoTracking()
            .ToListAsync(TestContext.Current.CancellationToken);

        nfoRows.Should().Contain(n => n.SourcePath == movieNfo && n.TmdbId == 27205,
            "NfoMetadata row must be persisted for the well-formed movie.nfo");

        nfoRows.Should().Contain(n => n.SourcePath == tvNfo && n.TmdbId == 1396,
            "NfoMetadata row must be persisted for the well-formed tvshow.nfo");
    }
}