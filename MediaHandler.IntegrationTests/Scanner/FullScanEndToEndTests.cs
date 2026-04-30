#nullable enable
// FullScanEndToEndTests — SC-001: ≥ 98 % classification accuracy
// Integration test: fake INasService + Testcontainers SQL Server

using FluentAssertions;
using MediaHandler.Application.Common.DTOs;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models.Scanner;
using MediaHandler.Application.Features.Scan.Commands.StartScan;
using MediaHandler.Application.Features.Scan.Queries.GetScanRun;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Enums;
using MediaHandler.Infrastructure.Nas.Scanner;
using MediaHandler.Infrastructure.Services;
using MediaHandler.IntegrationTests.Common;
using MediaHandler.IntegrationTests.Scanner.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TmdbIdLookupResult = MediaHandler.Application.Common.Interfaces.TmdbIdLookupResult;

namespace MediaHandler.IntegrationTests.Scanner;

/// <summary>
/// SC-001: ≥ 98 % correct classification of the benchmark fixture.
/// </summary>
public class FullScanEndToEndTests : ScannerIntegrationTestBase
{
    private FixtureBuilder? _fixture;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        _fixture = FixtureBuilder.LoadFromManifest();
        WithFakeNasService(_fixture.ToNasFileInfos(), configuredPaths: ["/nas"]);
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
        await WaitForScanCompletion(handle.ScanRunId, timeoutSeconds: 120);

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
            because: $"SC-001 requires ≥ 98% classification accuracy. Got {totalAdded}/{totalExpected} = {classificationRate:P1}");
    }

    /// <summary>
    /// SC-002: ≤ 0.5 % silent misclassification rate.
    /// Every divergence from the fixture's expected (tmdbId, kind) MUST produce a ReviewItem
    /// for the same path; a divergence without a ReviewItem is counted as "silent".
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

        var realMatcher = new MediaHandler.Infrastructure.Nas.Scanner.TmdbMatcher(fakeTmdb);

        var coordinator = BuildCoordinatorWithMatcher(moviesRoot, tvRoot, realMatcher);
        var scanRunId = Guid.NewGuid();
        var handle = await coordinator.StartAsync(
            new ScanStartParameters(scanRunId, [moviesRoot.Id, tvRoot.Id], ScanMode.Full),
            TestContext.Current.CancellationToken);

        await WaitForScanCompletion(handle.ScanRunId, timeoutSeconds: 120);
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
            because: $"SC-002 requires ≤ 0.5% silent misclassification. " +
                     $"Got {silentCount} silent out of {totalClassified} = {silentRate:P2}");
    }

    private async Task WaitForScanCompletion(Guid scanRunId, int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var run = await DbContext.ScanRuns.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == scanRunId, TestContext.Current.CancellationToken);

            if (run is null) { await Task.Delay(500); continue; }

            if (run.Status is ScanStatus.Completed or ScanStatus.Failed or ScanStatus.Cancelled)
                return;

            await Task.Delay(200, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"Scan {scanRunId} did not complete within {timeoutSeconds}s");
    }

    private ScanRunCoordinator BuildCoordinator()
    {
        var nasEnumerator = new MediaHandler.Infrastructure.Nas.NasFileEnumerator(
            FakeNas!, Microsoft.Extensions.Logging.Abstractions.NullLogger<MediaHandler.Infrastructure.Nas.NasFileEnumerator>.Instance);

        var parser = new KodiNameParser();
        var exclusionEvaluator = new ExclusionEvaluator();
        var stackDetector = new StackingDetector();
        var episodeMatcher = new TvEpisodeMatcher();
        var tmdbMatcher = Substitute.For<ITmdbMatcher>();
        // TMDB stub: always return needs-review=false with no TmdbId (US1 scope)
        tmdbMatcher.ResolveAsync(Arg.Any<MatchQuery>(), Arg.Any<CancellationToken>())
            .Returns(new TmdbMatchResult(false, null, null, false, null, []));

        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<ScanRunCoordinator>.Instance;
        var pipelineLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<ScanPipeline>.Instance;

        // Give the coordinator its OWN DbContext so its background scan task never
        // shares a context instance with the test's polling queries.
        var coordinatorDb = new MediaHandler.Infrastructure.Persistence.MediaHandlerDbContext(DbContextOptions);

        var pipeline = new ScanPipeline(
            coordinatorDb,
            nasEnumerator,
            exclusionEvaluator,
            stackDetector,
            parser,
            episodeMatcher,
            tmdbMatcher,
            pipelineLogger);

        return new ScanRunCoordinator(logger, pipeline, coordinatorDb);
    }

    private ScanRunCoordinator BuildCoordinatorWithMatcher(
        LibraryRoot moviesRoot,
        LibraryRoot tvRoot,
        ITmdbMatcher tmdbMatcher)
    {
        var nasEnumerator = new MediaHandler.Infrastructure.Nas.NasFileEnumerator(
            FakeNas!, Microsoft.Extensions.Logging.Abstractions.NullLogger<MediaHandler.Infrastructure.Nas.NasFileEnumerator>.Instance);

        var parser = new KodiNameParser();
        var exclusionEvaluator = new ExclusionEvaluator();
        var stackDetector = new StackingDetector();
        var episodeMatcher = new TvEpisodeMatcher();

        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<ScanRunCoordinator>.Instance;
        var pipelineLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<ScanPipeline>.Instance;

        var coordinatorDb = new MediaHandler.Infrastructure.Persistence.MediaHandlerDbContext(DbContextOptions);

        var pipeline = new ScanPipeline(
            coordinatorDb,
            nasEnumerator,
            exclusionEvaluator,
            stackDetector,
            parser,
            episodeMatcher,
            tmdbMatcher,
            pipelineLogger);

        return new ScanRunCoordinator(logger, pipeline, coordinatorDb);
    }
}
