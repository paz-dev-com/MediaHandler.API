// SC-008: Title-parsing fix — end-to-end validation that the 6 confirmed failing shows produce
// correct ParsedTitle values in the review queue after the scanner pipeline runs.
// No real TMDB calls are needed: the TMDB matcher is stubbed to return NeedsReview for all
// title searches, forcing every file onto the review queue where ParsedTitle is then inspected.
// Also includes a regression test for FR-016: resolved ReviewItems must not be re-flagged on re-scan.

using FluentAssertions;
using MediaHandler.Application.Common.DTOs;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models.Scanner;
using MediaHandler.Application.Features.Review.Commands.ResolveReviewItem;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Enums;
using MediaHandler.Infrastructure.Nas;
using MediaHandler.Infrastructure.Nas.Scanner;
using MediaHandler.Infrastructure.Persistence;
using MediaHandler.Infrastructure.Services;
using MediaHandler.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MediaHandler.IntegrationTests.Scanner;

/// <summary>
///     SC-008: End-to-end validation of the title-parsing fix.
///     Covers the 6 confirmed acceptance scenarios from the spec plus a release-tag-only edge case,
///     and includes a FR-016 regression guard (resolved paths not re-flagged on re-scan).
/// </summary>
public class Sc008_TitleParsingFix : ScannerIntegrationTestBase
{
    // =========================================================================
    // Acceptance scenario helpers
    // =========================================================================

    private static NasFileInfo Dir(string path) =>
        new(path, System.IO.Path.GetFileName(path), 0, null, DateTime.UtcNow, DateTime.UtcNow, true);

    private static NasFileInfo File(string path) =>
        new(path, System.IO.Path.GetFileName(path), 1_073_741_824, "MKV", DateTime.UtcNow, DateTime.UtcNow);

    // =========================================================================
    // Acceptance scenario 1 — Slow Horses
    // =========================================================================

    [Fact]
    public async Task Sc008_SlowHorses_ParsedTitleIsCorrect()
    {
        const string filePath =
            "/nas/Séries/Slow Horses/S03/Slow.Horses.S03E05.MULTi.1080p.WEBRip.x264.AC3-MULTiViSiON.mkv";

        var entries = new[]
        {
            Dir("/nas/Séries"),
            Dir("/nas/Séries/Slow Horses"),
            Dir("/nas/Séries/Slow Horses/S03"),
            File(filePath)
        };

        var parsedTitle = await RunAndGetParsedTitleAsync(entries, filePath);

        parsedTitle.Should().Be("Slow Horses",
            "dots before SxxExx must be replaced with spaces; no release tags appear before the marker");
    }

    // =========================================================================
    // Acceptance scenario 2 — Law and Order SVU (filename typo preserved)
    // =========================================================================

    [Fact]
    public async Task Sc008_LawAndOrderSvu_ParsedTitlePreservesFilenameContent()
    {
        const string filePath =
            "/nas/Séries/Law and Order/SVU/S19/Law.and.Order.SUV.S19E23.FRENCH.DVDRip.XviD-Wawacity.tv.avi";

        var entries = new[]
        {
            Dir("/nas/Séries"),
            Dir("/nas/Séries/Law and Order"),
            Dir("/nas/Séries/Law and Order/SVU"),
            Dir("/nas/Séries/Law and Order/SVU/S19"),
            File(filePath)
        };

        var parsedTitle = await RunAndGetParsedTitleAsync(entries, filePath);

        // Filename typo "SUV" is preserved in ParsedTitle (filename content is authoritative)
        parsedTitle.Should().Be("Law and Order SUV",
            "the parsed title reflects the filename text before SxxExx — the SUV/SVU typo is expected");
    }

    // =========================================================================
    // Acceptance scenario 3 — The Nanny (French dub: Une Nounou Denfer)
    // =========================================================================

    [Fact]
    public async Task Sc008_TheNanny_FrenchDubTitle_NoReleaseTags()
    {
        const string filePath =
            "/nas/Séries/The Nanny/Une.Nounou.Denfer.S04.MULTi.DVDRIP.x264-ETAY/Une.Nounou.Denfer.S04E10.MULTi.DVDRIP.x264-ETAY.mkv";

        var entries = new[]
        {
            Dir("/nas/Séries"),
            Dir("/nas/Séries/The Nanny"),
            Dir("/nas/Séries/The Nanny/Une.Nounou.Denfer.S04.MULTi.DVDRIP.x264-ETAY"),
            File(filePath)
        };

        var parsedTitle = await RunAndGetParsedTitleAsync(entries, filePath);

        parsedTitle.Should().Be("Une Nounou Denfer",
            "dots before SxxExx must become spaces; MULTi/DVDRIP/x264 appear after SxxExx and must not appear in title");
    }

    // =========================================================================
    // Acceptance scenario 4 — The Killing US 2011 (year preserved before SxxExx)
    // =========================================================================

    [Fact]
    public async Task Sc008_TheKillingUS_YearBeforeSxxExx_Preserved()
    {
        const string filePath =
            "/nas/Séries/The Killing US/S03/The.Killing.US.2011.S03E10.1080p.MULTi.WEB-DL.AvALoN.mkv";

        var entries = new[]
        {
            Dir("/nas/Séries"),
            Dir("/nas/Séries/The Killing US"),
            Dir("/nas/Séries/The Killing US/S03"),
            File(filePath)
        };

        var parsedTitle = await RunAndGetParsedTitleAsync(entries, filePath);

        parsedTitle.Should().Be("The Killing US 2011",
            "year 2011 precedes SxxExx and must be preserved in the title for TMDB disambiguation");
    }

    // =========================================================================
    // Acceptance scenario 5 — The Wire (French title: Sur écoute, accented é preserved)
    // =========================================================================

    [Fact]
    public async Task Sc008_TheWire_AccentedFrenchTitle_Preserved()
    {
        const string filePath =
            "/nas/Séries/The Wire/The Wire/Sur écoute S04E01 - La fin de l'été.mkv";

        var entries = new[]
        {
            Dir("/nas/Séries"),
            Dir("/nas/Séries/The Wire"),
            Dir("/nas/Séries/The Wire/The Wire"),
            File(filePath)
        };

        var parsedTitle = await RunAndGetParsedTitleAsync(entries, filePath);

        parsedTitle.Should().Be("Sur écoute",
            "accented character é must survive the dot→space replacement and title-cleaning pipeline");
    }

    // =========================================================================
    // Acceptance scenario 6 — Release-tag-only edge case
    // =========================================================================

    [Fact]
    public async Task Sc008_ReleaseTagsBeforeSxxExx_AreStripped()
    {
        const string filePath =
            "/nas/Séries/My Show/S01/My.Show.1080p.MULTi.WEBRip.S01E01.mkv";

        var entries = new[]
        {
            Dir("/nas/Séries"),
            Dir("/nas/Séries/My Show"),
            Dir("/nas/Séries/My Show/S01"),
            File(filePath)
        };

        var parsedTitle = await RunAndGetParsedTitleAsync(entries, filePath);

        parsedTitle.Should().Be("My Show",
            "quality/language/source tags that appear before SxxExx must be stripped from the parsed title");
    }

    // =========================================================================
    // FR-016 regression: resolved ReviewItem must NOT be re-flagged on re-scan
    // =========================================================================

    [Fact]
    public async Task Fr016_RescanSuppression_ResolvedReviewItem_NotReFlagged()
    {
        const string filePath =
            "/nas/Séries/The Wire/The Wire/Sur écoute S04E01 - La fin de l'été.mkv";
        const int resolvedTmdbId = 1438;

        var entries = new[]
        {
            Dir("/nas/Séries"),
            Dir("/nas/Séries/The Wire"),
            Dir("/nas/Séries/The Wire/The Wire"),
            File(filePath)
        };

        WithFakeNasService(entries, ["/nas"]);

        var tvRoot = new LibraryRoot
        {
            Path = "/nas/Séries",
            Kind = LibraryRootKind.TvShows,
            IsEnabled = true
        };
        DbContext.LibraryRoots.Add(tvRoot);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // First scan: TMDB searches return NeedsReview, ReviewItem created
        var matcher1 = Substitute.For<ITmdbMatcher>();
        matcher1.ResolveAsync(Arg.Any<MatchQuery>(), Arg.Any<CancellationToken>())
            .Returns(new TmdbMatchResult(false, null, null, true, ReviewReason.NoTmdbResult, []));

        var coordinator1 = BuildCoordinatorWithMatcher(matcher1);
        var handle1 = await coordinator1.StartAsync(
            new ScanStartParameters(Guid.NewGuid(), [tvRoot.Id], ScanMode.Full),
            TestContext.Current.CancellationToken);
        await WaitForScanCompletionAsync(handle1.ScanRunId, 60);

        var reviewItem = await DbContext.ReviewItems
            .FirstOrDefaultAsync(r => r.FilePath == filePath && r.Status == ReviewStatus.Open,
                TestContext.Current.CancellationToken);
        reviewItem.Should().NotBeNull("first scan should create a ReviewItem");

        // Resolve the ReviewItem
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.OktaId.Returns("test-admin");
        var resolveTmdb = Substitute.For<ITmdbService>();
        resolveTmdb.GetTvShowByIdAsync(resolvedTmdbId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new TmdbIdLookupResult(resolvedTmdbId, MediaType.TvShow, "The Wire", 2002, null));

        var handler = new ResolveReviewItemCommandHandler(DbContext, resolveTmdb, currentUser);
        var resolveResult = await handler.Handle(
            new ResolveReviewItemCommand(reviewItem!.Id, ReviewResolutionAction.Assign, resolvedTmdbId, MediaType.TvShow),
            TestContext.Current.CancellationToken);
        resolveResult.IsSuccess.Should().BeTrue();

        await DbContext.Entry(reviewItem).ReloadAsync(TestContext.Current.CancellationToken);
        reviewItem.Status.Should().Be(ReviewStatus.Resolved);

        // Second scan: resolved path must not produce a new open ReviewItem
        var coordinatorDb2 = new MediaHandlerDbContext(DbContextOptions);
        WithFakeNasService(entries, ["/nas"]);

        var matcher2 = Substitute.For<ITmdbMatcher>();
        matcher2.ResolveAsync(Arg.Any<MatchQuery>(), Arg.Any<CancellationToken>())
            .Returns(new TmdbMatchResult(false, null, null, true, ReviewReason.NoTmdbResult, []));

        var pipeline2 = new ScanPipeline(
            coordinatorDb2,
            new NasFileEnumerator(FakeNas!, NullLogger<NasFileEnumerator>.Instance),
            new ExclusionEvaluator(),
            new StackingDetector(),
            new KodiNameParser(),
            new TvEpisodeMatcher(),
            matcher2,
            NullLogger<ScanPipeline>.Instance);

        var coordinator2 = CreateScanRunCoordinator(pipeline2, coordinatorDb2);
        var handle2 = await coordinator2.StartAsync(
            new ScanStartParameters(Guid.NewGuid(), [tvRoot.Id], ScanMode.Full),
            TestContext.Current.CancellationToken);
        await WaitForScanCompletionAsync(handle2.ScanRunId, 60);

        var newOpenItems = await DbContext.ReviewItems
            .AsNoTracking()
            .CountAsync(r => r.FilePath == filePath && r.Status == ReviewStatus.Open,
                TestContext.Current.CancellationToken);

        newOpenItems.Should().Be(0,
            "FR-016: a previously resolved ReviewItem must not be re-created on re-scan when the path is unchanged");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private async Task<string?> RunAndGetParsedTitleAsync(
        NasFileInfo[] entries, string targetFilePath)
    {
        WithFakeNasService(entries, ["/nas"]);

        var ct = TestContext.Current.CancellationToken;

        var tvRoot = new LibraryRoot
        {
            Path = "/nas/Séries",
            Kind = LibraryRootKind.TvShows,
            IsEnabled = true
        };
        DbContext.LibraryRoots.Add(tvRoot);
        await DbContext.SaveChangesAsync(ct);

        // Stub TMDB: always NeedsReview so every file reaches the review queue
        var matcher = Substitute.For<ITmdbMatcher>();
        matcher.ResolveAsync(Arg.Any<MatchQuery>(), Arg.Any<CancellationToken>())
            .Returns(new TmdbMatchResult(false, null, null, true, ReviewReason.NoTmdbResult, []));

        var coordinator = BuildCoordinatorWithMatcher(matcher);
        var handle = await coordinator.StartAsync(
            new ScanStartParameters(Guid.NewGuid(), [tvRoot.Id], ScanMode.Full), ct);

        await WaitForScanCompletionAsync(handle.ScanRunId, 60);

        var reviewItem = await DbContext.ReviewItems
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.FilePath == targetFilePath, ct);

        return reviewItem?.ParsedTitle;
    }

    private async Task WaitForScanCompletionAsync(Guid scanRunId, int timeoutSeconds)
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

    private ScanRunCoordinator BuildCoordinatorWithMatcher(ITmdbMatcher matcher)
    {
        var coordinatorDb = new MediaHandlerDbContext(DbContextOptions);

        var pipeline = new ScanPipeline(
            coordinatorDb,
            new NasFileEnumerator(FakeNas!, NullLogger<NasFileEnumerator>.Instance),
            new ExclusionEvaluator(),
            new StackingDetector(),
            new KodiNameParser(),
            new TvEpisodeMatcher(),
            matcher,
            NullLogger<ScanPipeline>.Instance);

        return CreateScanRunCoordinator(pipeline, coordinatorDb);
    }
}

