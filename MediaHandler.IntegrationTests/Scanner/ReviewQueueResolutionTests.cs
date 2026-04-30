#nullable enable
// ReviewQueueResolutionTests — integration test for the full review-queue round-trip.
// Verifies: scan → review item created → admin resolves → next scan honours resolution.

using FluentAssertions;
using MediaHandler.Application.Common.DTOs;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models.Scanner;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Enums;
using MediaHandler.Infrastructure.Nas.Scanner;
using MediaHandler.Infrastructure.Services;
using MediaHandler.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using TmdbIdLookupResult = MediaHandler.Application.Common.Interfaces.TmdbIdLookupResult;

namespace MediaHandler.IntegrationTests.Scanner;

/// <summary>
/// Integration test for the review queue resolution round-trip.
/// Scenario: scan → review item created → POST resolve → re-scan respects resolution (no re-flag).
/// </summary>
public class ReviewQueueResolutionTests : ScannerIntegrationTestBase
{
    private const string MoviePath = "/nas/Movies/Ambiguous Title/Ambiguous.Title.mkv";
    private const string ResolvedTmdbTitle = "Ambiguous Title";
    private const int ResolvedTmdbId = 55555;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        // Seed a single ambiguous-title movie that will not resolve automatically
        var entries = new[]
        {
            new NasFileInfo("/nas/Movies", "Movies", 0, null, DateTime.UtcNow, DateTime.UtcNow, IsDirectory: true),
            new NasFileInfo("/nas/Movies/Ambiguous Title", "Ambiguous Title", 0, null, DateTime.UtcNow, DateTime.UtcNow, IsDirectory: true),
            new NasFileInfo(MoviePath, "Ambiguous.Title.mkv", 1_073_741_824, "MKV", DateTime.UtcNow, DateTime.UtcNow)
        };

        WithFakeNasService(entries, configuredPaths: ["/nas"]);
    }

    [Fact]
    public async Task ScanThenResolve_ThenRescan_DoesNotReCreateReviewItem()
    {
        var ct = TestContext.Current.CancellationToken;

        // ── Step 1: Run scan with a TMDB service that returns multiple candidates
        //            (forces MultipleCandidates → ReviewItem)
        var fakeTmdbFirstScan = Substitute.For<ITmdbService>();
        fakeTmdbFirstScan.SearchCandidatesAsync(
                Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<MediaType?>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([
                new TmdbSearchCandidate(ResolvedTmdbId, MediaType.Film, ResolvedTmdbTitle, 2020, 80m, null),
                new TmdbSearchCandidate(55556, MediaType.Film, "Another Ambiguous Title", 2019, 78m, null)
            ]);

        var moviesRoot = new LibraryRoot
        {
            Path = "/nas/Movies",
            Kind = LibraryRootKind.Movies,
            IsEnabled = true
        };
        DbContext.LibraryRoots.Add(moviesRoot);
        await DbContext.SaveChangesAsync(ct);

        var matcher1 = new TmdbMatcher(fakeTmdbFirstScan);
        var coordinator1 = BuildCoordinator(matcher1);

        var handle1 = await coordinator1.StartAsync(
            new ScanStartParameters(Guid.NewGuid(), [moviesRoot.Id], ScanMode.Full), ct);

        await WaitForScanCompletion(handle1.ScanRunId, 60, ct);

        // Verify that a ReviewItem was created for the ambiguous file
        var reviewItem = await DbContext.ReviewItems
            .FirstOrDefaultAsync(r => r.FilePath == MoviePath && r.Status == ReviewStatus.Open, ct);

        reviewItem.Should().NotBeNull(
            because: "the ambiguous-candidate file should produce a ReviewItem after the first scan");
        reviewItem!.Reason.Should().Be(ReviewReason.MultipleCandidates);

        // ── Step 2: Resolve the review item via the command handler
        var resolveHandler = new Application.Features.Review.Commands.ResolveReviewItem.ResolveReviewItemCommandHandler(
            DbContext,
            Substitute.For<ITmdbService>(),
            Substitute.For<ICurrentUserService>());

        // Pre-configure TMDB for the resolve step
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.OktaId.Returns("admin-test");

        var resolveFakeTmdb = Substitute.For<ITmdbService>();
        resolveFakeTmdb.GetMovieByIdAsync(ResolvedTmdbId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new TmdbIdLookupResult(ResolvedTmdbId, MediaType.Film, ResolvedTmdbTitle, 2020, null));

        var resolveHandlerWired = new Application.Features.Review.Commands.ResolveReviewItem.ResolveReviewItemCommandHandler(
            DbContext,
            resolveFakeTmdb,
            currentUser);

        var resolveResult = await resolveHandlerWired.Handle(
            new Application.Features.Review.Commands.ResolveReviewItem.ResolveReviewItemCommand(
                reviewItem.Id,
                ReviewResolutionAction.Assign,
                TmdbId: ResolvedTmdbId,
                Kind: MediaType.Film),
            ct);

        resolveResult.IsSuccess.Should().BeTrue("the Assign resolve should succeed");

        // Refresh: review item should now be Resolved
        await DbContext.Entry(reviewItem).ReloadAsync(ct);
        reviewItem.Status.Should().Be(ReviewStatus.Resolved);
        reviewItem.ResolvedTmdbId.Should().Be(ResolvedTmdbId);

        // ── Step 3: Re-scan — the pipeline should read the saved resolution and NOT create a new ReviewItem
        var fakeTmdbSecondScan = Substitute.For<ITmdbService>();

        // This time, still returns multiple candidates — but the pipeline should skip title-search
        // for already-resolved paths
        fakeTmdbSecondScan.SearchCandidatesAsync(
                Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<MediaType?>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([
                new TmdbSearchCandidate(ResolvedTmdbId, MediaType.Film, ResolvedTmdbTitle, 2020, 80m, null),
                new TmdbSearchCandidate(55556, MediaType.Film, "Another Ambiguous Title", 2019, 78m, null)
            ]);

        fakeTmdbSecondScan.GetMovieByIdAsync(ResolvedTmdbId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new TmdbIdLookupResult(ResolvedTmdbId, MediaType.Film, ResolvedTmdbTitle, 2020, null));

        var matcher2 = new TmdbMatcher(fakeTmdbSecondScan);

        // Use a fresh coordinator with a separate DbContext
        var coordinatorDb2 = new MediaHandler.Infrastructure.Persistence.MediaHandlerDbContext(DbContextOptions);
        var coordinator2 = BuildCoordinatorWithDb(matcher2, coordinatorDb2);

        var handle2 = await coordinator2.StartAsync(
            new ScanStartParameters(Guid.NewGuid(), [moviesRoot.Id], ScanMode.Full), ct);

        await WaitForScanCompletion(handle2.ScanRunId, 60, ct);

        // After the second scan, there should be NO new Open review items for this path
        var openReviewItemsAfterRescan = await DbContext.ReviewItems
            .AsNoTracking()
            .Where(r => r.FilePath == MoviePath && r.Status == ReviewStatus.Open)
            .CountAsync(ct);

        openReviewItemsAfterRescan.Should().Be(0,
            because: "the pipeline should re-use the saved resolution rather than re-flagging the file");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private async Task WaitForScanCompletion(Guid scanRunId, int timeoutSeconds, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var run = await DbContext.ScanRuns.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == scanRunId, ct);

            if (run is null) { await Task.Delay(500, ct); continue; }

            if (run.Status is ScanStatus.Completed or ScanStatus.Failed or ScanStatus.Cancelled)
                return;

            await Task.Delay(200, ct);
        }

        throw new TimeoutException($"Scan {scanRunId} did not complete within {timeoutSeconds}s");
    }

    private ScanRunCoordinator BuildCoordinator(ITmdbMatcher matcher)
        => BuildCoordinatorWithDb(matcher, new MediaHandler.Infrastructure.Persistence.MediaHandlerDbContext(DbContextOptions));

    private ScanRunCoordinator BuildCoordinatorWithDb(ITmdbMatcher matcher, MediaHandler.Infrastructure.Persistence.MediaHandlerDbContext coordinatorDb)
    {
        var nasEnumerator = new MediaHandler.Infrastructure.Nas.NasFileEnumerator(
            FakeNas!, Microsoft.Extensions.Logging.Abstractions.NullLogger<MediaHandler.Infrastructure.Nas.NasFileEnumerator>.Instance);

        var parser = new KodiNameParser();
        var exclusionEvaluator = new ExclusionEvaluator();
        var stackDetector = new StackingDetector();
        var episodeMatcher = new TvEpisodeMatcher();

        var pipelineLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<ScanPipeline>.Instance;
        var coordinatorLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<ScanRunCoordinator>.Instance;

        var pipeline = new ScanPipeline(
            coordinatorDb,
            nasEnumerator,
            exclusionEvaluator,
            stackDetector,
            parser,
            episodeMatcher,
            matcher,
            pipelineLogger);

        return new ScanRunCoordinator(coordinatorLogger, pipeline, coordinatorDb);
    }
}

