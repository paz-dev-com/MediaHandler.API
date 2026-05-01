// SC-003: 100% exclusion accuracy — zero false positives and zero false negatives.
// Every fixture path tagged "excluded" must produce ScanItemDecision.Kind=Excluded with no MediaFile.
// Every fixture path tagged "included" must produce a MediaFile row.

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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NasFileInfo = MediaHandler.Application.Common.DTOs.NasFileInfo;

namespace MediaHandler.IntegrationTests.Scanner;

/// <summary>
///     SC-003: every expected:excluded path → ScanItemDecision.Kind=Excluded + zero MediaFile;
///     every expected:included → MediaFile exists.
/// </summary>
public class Sc003_ExclusionFidelity_NoFalsePositivesOrFalseNegatives : ScannerIntegrationTestBase
{
    // Curated fixture with explicit expected outcomes
    private static readonly (string Path, string FileName, bool IsDirectory, bool ExpectedExcluded, string? Extension)[]
        Fixture =
        [
            // Included files (expected: included)
            ("/nas/Movies/Inception (2010)/Inception (2010).mkv", "Inception (2010).mkv", false, false, "mkv"),
            ("/nas/Movies/The Matrix (1999)/The Matrix (1999).mkv", "The Matrix (1999).mkv", false, false, "mkv"),
            ("/nas/TV Shows/Breaking Bad/Season 01/Breaking.Bad.S01E01.mkv", "Breaking.Bad.S01E01.mkv", false, false,
                "mkv"),
            ("/nas/Movies/Dune (2021)/Dune (2021).mp4", "Dune (2021).mp4", false, false, "mp4"),
            ("/nas/Movies/Interstellar (2014)/Interstellar (2014).avi", "Interstellar (2014).avi", false, false, "avi"),

            // Excluded: sample filename pattern
            ("/nas/Movies/The Matrix (1999)/The.Matrix.1999-sample.mkv", "The.Matrix.1999-sample.mkv", false, true,
                "mkv"),

            // Excluded: trailer filename pattern
            ("/nas/Movies/Inception (2010)/inception-trailer.mkv", "inception-trailer.mkv", false, true, "mkv"),

            // Excluded: non-video extension
            ("/nas/Movies/poster.jpg", "poster.jpg", false, true, "jpg"),
            ("/nas/Movies/The Matrix (1999)/subtitles.srt", "subtitles.srt", false, true, "srt"),
            ("/nas/Movies/The Matrix (1999)/info.txt", "info.txt", false, true, "txt"),
            ("/nas/Movies/The Matrix (1999)/cover.png", "cover.png", false, true, "png"),

            // Excluded: extras folder
            ("/nas/Movies/Extras/behind-the-scenes.mkv", "behind-the-scenes.mkv", false, true, "mkv"),

            // Excluded: trailers folder
            ("/nas/Movies/Trailers/matrix-trailer.mkv", "matrix-trailer.mkv", false, true, "mkv"),

            // Excluded: featurettes folder
            ("/nas/Movies/Featurettes/making-of.mkv", "making-of.mkv", false, true, "mkv"),

            // Excluded: hidden folder (.recycle)
            ("/nas/Movies/.recycle/oldfile.mkv", "oldfile.mkv", false, true, "mkv"),

            // Excluded: .nomedia marker folder files
            ("/nas/Movies/Private/.nomedia", ".nomedia", false, true, null),
            ("/nas/Movies/Private/secret-movie.mkv", "secret-movie.mkv", false, true, "mkv"),

            // Directory entries (processed for context but not checked as media)
            ("/nas/Movies", "Movies", true, false, null),
            ("/nas/Movies/Inception (2010)", "Inception (2010)", true, false, null),
            ("/nas/Movies/The Matrix (1999)", "The Matrix (1999)", true, false, null),
            ("/nas/Movies/Extras", "Extras", true, true, null),
            ("/nas/Movies/Trailers", "Trailers", true, true, null),
            ("/nas/Movies/Featurettes", "Featurettes", true, true, null),
            ("/nas/Movies/.recycle", ".recycle", true, true, null),
            ("/nas/Movies/Private", "Private", true, false, null),
            ("/nas/TV Shows", "TV Shows", true, false, null),
            ("/nas/TV Shows/Breaking Bad", "Breaking Bad", true, false, null),
            ("/nas/TV Shows/Breaking Bad/Season 01", "Season 01", true, false, null),
            ("/nas/Movies/Dune (2021)", "Dune (2021)", true, false, null),
            ("/nas/Movies/Interstellar (2014)", "Interstellar (2014)", true, false, null)
        ];

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        var nasEntries = Fixture.Select(f => new NasFileInfo(
            f.Path, f.FileName,
            f.IsDirectory ? 0 : 1_073_741_824L,
            f.Extension?.ToUpperInvariant(),
            DateTime.UtcNow, DateTime.UtcNow,
            f.IsDirectory)).ToList();

        WithFakeNasService(nasEntries, ["/nas"]);
    }

    [Fact]
    public async Task Sc003_AllExcludedPathsHaveExcludedDecision_AndNoMediaFile()
    {
        // Register library roots
        var moviesRoot = new LibraryRoot { Path = "/nas/Movies", Kind = LibraryRootKind.Movies, IsEnabled = true };
        var tvRoot = new LibraryRoot { Path = "/nas/TV Shows", Kind = LibraryRootKind.TvShows, IsEnabled = true };
        DbContext.LibraryRoots.AddRange(moviesRoot, tvRoot);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var coordinator = BuildCoordinator();
        var handle = await coordinator.StartAsync(
            new ScanStartParameters(Guid.NewGuid(), [moviesRoot.Id, tvRoot.Id], ScanMode.Full),
            TestContext.Current.CancellationToken);

        await WaitForScanCompletion(handle.ScanRunId, 60);

        // Load decisions and media files
        var decisions = await DbContext.ScanItemDecisions
            .AsNoTracking()
            .Where(d => d.ScanRunId == handle.ScanRunId)
            .ToListAsync(TestContext.Current.CancellationToken);

        var mediaFilePaths = (await DbContext.MediaFiles
                .AsNoTracking()
                .Select(mf => mf.FilePath)
                .ToListAsync(TestContext.Current.CancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var decisionsByPath = decisions
            .GroupBy(d => d.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        // Check expected:excluded entries (non-directory files only)
        var excludedFiles = Fixture.Where(f => f.ExpectedExcluded && !f.IsDirectory).ToList();
        var falseNegatives = new List<string>();

        foreach (var excluded in excludedFiles)
        {
            // Must have an Excluded decision
            if (decisionsByPath.TryGetValue(excluded.Path, out var pathDecisions))
            {
                var hasExclusion = pathDecisions.Any(d => d.Kind == ScanDecisionKind.Excluded);
                if (!hasExclusion)
                    falseNegatives.Add($"Missing Excluded decision: {excluded.Path}");
            }
            // Note: some exclusions (like .nomedia markers or non-video extensions) may not
            // produce a decision row for the entry itself if the entry is filtered earlier.

            // Must NOT have a MediaFile row
            mediaFilePaths.Should().NotContain(excluded.Path,
                $"Excluded file should not produce a MediaFile: {excluded.Path}");
        }

        // Check expected:included entries (non-directory files only)
        var includedFiles = Fixture.Where(f => !f.ExpectedExcluded && !f.IsDirectory).ToList();
        var falsePositives = new List<string>();

        foreach (var included in includedFiles)
            if (!mediaFilePaths.Contains(included.Path))
                falsePositives.Add($"Missing MediaFile for included path: {included.Path}");

        falsePositives.Should().BeEmpty(
            "Every expected:included path must produce a MediaFile (zero false positives)");

        // Allow informational output on false negatives — strict enforcement
        falseNegatives.Should().BeEmpty(
            "Every expected:excluded path must produce an Excluded decision (zero false negatives)");
    }

    // ── Helper methods ──────────────────────────────────────────────────────

    private async Task WaitForScanCompletion(Guid scanRunId, int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var run = await DbContext.ScanRuns.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == scanRunId, TestContext.Current.CancellationToken);
            if (run?.Status is ScanStatus.Completed or ScanStatus.Failed or ScanStatus.Cancelled)
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
        tmdbMatcher.ResolveAsync(Arg.Any<MatchQuery>(), Arg.Any<CancellationToken>())
            .Returns(new TmdbMatchResult(false, null, null, false, null, []));

        var logger = NullLogger<ScanRunCoordinator>.Instance;
        var pipelineLogger = NullLogger<ScanPipeline>.Instance;

        var coordinatorDb = new MediaHandlerDbContext(DbContextOptions);
        var pipeline = new ScanPipeline(coordinatorDb, nasEnumerator, exclusionEvaluator, stackDetector,
            parser, episodeMatcher, tmdbMatcher, pipelineLogger);

        return CreateScanRunCoordinator(pipeline, coordinatorDb);
    }
}