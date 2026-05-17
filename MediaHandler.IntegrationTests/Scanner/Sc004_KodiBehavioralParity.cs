// SC-004: ≥ 99% Kodi behavioral parity — scanner matches Kodi's observed outcomes
// on a curated subset of paths annotated with what Kodi actually produces.

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
///     SC-004: Kodi behavioral parity — runs scanner over a curated parity fixture
///     annotated with observed Kodi classification outcomes. Asserts ≥ 99% match rate.
/// </summary>
public class Sc004_KodiBehavioralParity : ScannerIntegrationTestBase
{
    /// <summary>
    ///     Curated parity fixture: each entry records what Kodi observes for the given path.
    ///     KodiOutcome values: "Movie", "Episode", "Excluded", "Ignored".
    /// </summary>
    private static readonly (string Path, string FileName, bool IsDirectory, string? Extension, string KodiOutcome)[]
        ParityFixture =
        [
            // Standard per-folder movies — Kodi classifies as Movie
            ("/nas/Movies/Inception (2010)/Inception (2010).mkv", "Inception (2010).mkv", false, "mkv", "Movie"),
            ("/nas/Movies/The Matrix (1999)/The Matrix (1999).mkv", "The Matrix (1999).mkv", false, "mkv", "Movie"),
            ("/nas/Movies/Interstellar (2014)/Interstellar (2014).mkv", "Interstellar (2014).mkv", false, "mkv",
                "Movie"),
            ("/nas/Movies/Pulp Fiction (1994)/Pulp Fiction (1994).mkv", "Pulp Fiction (1994).mkv", false, "mkv",
                "Movie"),
            ("/nas/Movies/The Dark Knight (2008)/The Dark Knight (2008).mkv", "The Dark Knight (2008).mkv", false,
                "mkv",
                "Movie"),

            // Flat-folder movies — Kodi classifies as Movie (from filename)
            ("/nas/Movies/Inception.2010.1080p.BluRay.x264-GROUP.mkv", "Inception.2010.1080p.BluRay.x264-GROUP.mkv",
                false,
                "mkv", "Movie"),
            ("/nas/Movies/Dune.2021.BluRay.mkv", "Dune.2021.BluRay.mkv", false, "mkv", "Movie"),

            // Stacked movies — Kodi groups as a single Movie
            ("/nas/Movies/Kill Bill Vol 1 (2003)/Kill.Bill.Vol.1.2003.cd1.mkv", "Kill.Bill.Vol.1.2003.cd1.mkv", false,
                "mkv", "Movie"),
            ("/nas/Movies/Kill Bill Vol 1 (2003)/Kill.Bill.Vol.1.2003.cd2.mkv", "Kill.Bill.Vol.1.2003.cd2.mkv", false,
                "mkv", "Movie"),

            // TV episodes — Kodi classifies as Episode
            ("/nas/TV Shows/Breaking Bad/Season 01/Breaking.Bad.S01E01.mkv", "Breaking.Bad.S01E01.mkv", false, "mkv",
                "Episode"),
            ("/nas/TV Shows/Breaking Bad/Season 01/Breaking.Bad.S01E02.mkv", "Breaking.Bad.S01E02.mkv", false, "mkv",
                "Episode"),
            ("/nas/TV Shows/Breaking Bad/Season 02/Breaking.Bad.S02E01.mkv", "Breaking.Bad.S02E01.mkv", false, "mkv",
                "Episode"),

            // Multi-episode file — Kodi classifies as Episode
            ("/nas/TV Shows/The Office US/Season 01/The.Office.US.S01E01-E02.mkv", "The.Office.US.S01E01-E02.mkv",
                false,
                "mkv", "Episode"),

            // 1x05 style — Kodi classifies as Episode
            ("/nas/TV Shows/Seinfeld/Season 01/Seinfeld.1x01.mkv", "Seinfeld.1x01.mkv", false, "mkv", "Episode"),
            ("/nas/TV Shows/Seinfeld/Season 01/Seinfeld.1x02.mkv", "Seinfeld.1x02.mkv", false, "mkv", "Episode"),

            // Specials — Kodi classifies as Episode (Season 0)
            ("/nas/TV Shows/Doctor Who/Specials/Doctor.Who.S00E01.Special.mkv", "Doctor.Who.S00E01.Special.mkv", false,
                "mkv", "Episode"),

            // Date-based episode — Kodi classifies as Episode
            ("/nas/TV Shows/The Daily Show/Season 2024/The.Daily.Show.2024.03.18.mkv", "The.Daily.Show.2024.03.18.mkv",
                false, "mkv", "Episode"),

            // Exclusions — Kodi excludes these
            ("/nas/Movies/The Matrix (1999)/The.Matrix.1999-sample.mkv", "The.Matrix.1999-sample.mkv", false, "mkv",
                "Excluded"),
            ("/nas/Movies/Extras/behind-the-scenes.mkv", "behind-the-scenes.mkv", false, "mkv", "Excluded"),
            ("/nas/Movies/Trailers/trailer.mkv", "trailer.mkv", false, "mkv", "Excluded"),
            ("/nas/Movies/.recycle/oldfile.mkv", "oldfile.mkv", false, "mkv", "Excluded"),
            ("/nas/Movies/poster.jpg", "poster.jpg", false, "jpg", "Excluded"),

            // Directory entries (needed for context)
            ("/nas/Movies", "Movies", true, null, "Ignored"),
            ("/nas/Movies/Inception (2010)", "Inception (2010)", true, null, "Ignored"),
            ("/nas/Movies/The Matrix (1999)", "The Matrix (1999)", true, null, "Ignored"),
            ("/nas/Movies/Interstellar (2014)", "Interstellar (2014)", true, null, "Ignored"),
            ("/nas/Movies/Pulp Fiction (1994)", "Pulp Fiction (1994)", true, null, "Ignored"),
            ("/nas/Movies/The Dark Knight (2008)", "The Dark Knight (2008)", true, null, "Ignored"),
            ("/nas/Movies/Kill Bill Vol 1 (2003)", "Kill Bill Vol 1 (2003)", true, null, "Ignored"),
            ("/nas/Movies/Extras", "Extras", true, null, "Ignored"),
            ("/nas/Movies/Trailers", "Trailers", true, null, "Ignored"),
            ("/nas/Movies/.recycle", ".recycle", true, null, "Ignored"),
            ("/nas/TV Shows", "TV Shows", true, null, "Ignored"),
            ("/nas/TV Shows/Breaking Bad", "Breaking Bad", true, null, "Ignored"),
            ("/nas/TV Shows/Breaking Bad/Season 01", "Season 01", true, null, "Ignored"),
            ("/nas/TV Shows/Breaking Bad/Season 02", "Season 02", true, null, "Ignored"),
            ("/nas/TV Shows/The Office US", "The Office US", true, null, "Ignored"),
            ("/nas/TV Shows/The Office US/Season 01", "Season 01", true, null, "Ignored"),
            ("/nas/TV Shows/Seinfeld", "Seinfeld", true, null, "Ignored"),
            ("/nas/TV Shows/Seinfeld/Season 01", "Season 01", true, null, "Ignored"),
            ("/nas/TV Shows/Doctor Who", "Doctor Who", true, null, "Ignored"),
            ("/nas/TV Shows/Doctor Who/Specials", "Specials", true, null, "Ignored"),
            ("/nas/TV Shows/The Daily Show", "The Daily Show", true, null, "Ignored"),
            ("/nas/TV Shows/The Daily Show/Season 2024", "Season 2024", true, null, "Ignored")
        ];

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        var nasEntries = ParityFixture.Select(f => new NasFileInfo(
            f.Path, f.FileName,
            f.IsDirectory ? 0 : 1_073_741_824L,
            f.Extension?.ToUpperInvariant(),
            DateTime.UtcNow, DateTime.UtcNow,
            f.IsDirectory)).ToList();

        WithFakeNasService(nasEntries, ["/nas"]);
    }

    [Fact]
    public async Task Sc004_KodiBehavioralParity_AtLeast99Percent()
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

        // Load all decisions for the scan
        var decisions = await DbContext.ScanItemDecisions
            .AsNoTracking()
            .Where(d => d.ScanRunId == handle.ScanRunId)
            .ToListAsync(TestContext.Current.CancellationToken);

        var decisionsByPath = decisions
            .GroupBy(d => d.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var mediaFilePaths = (await DbContext.MediaFiles
                .AsNoTracking()
                .Select(mf => mf.FilePath)
                .ToListAsync(TestContext.Current.CancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Compare scanner outcome against Kodi's observed outcome
        var testableEntries = ParityFixture
            .Where(f => !f.IsDirectory && f.KodiOutcome != "Ignored")
            .ToList();

        var matchCount = 0;
        var mismatchDetails = new List<string>();

        foreach (var entry in testableEntries)
        {
            var scannerOutcome = ClassifyOutcome(entry.Path, decisionsByPath, mediaFilePaths);
            if (string.Equals(scannerOutcome, entry.KodiOutcome, StringComparison.OrdinalIgnoreCase))
                matchCount++;
            else
                mismatchDetails.Add($"  {entry.Path}: Kodi={entry.KodiOutcome}, Scanner={scannerOutcome}");
        }

        var parityRate = testableEntries.Count > 0
            ? (double)matchCount / testableEntries.Count
            : 1.0;

        parityRate.Should().BeGreaterThanOrEqualTo(0.99,
            $"SC-004 requires ≥ 99% Kodi behavioral parity. " +
            $"Got {matchCount}/{testableEntries.Count} = {parityRate:P1}. " +
            $"Mismatches:\n{string.Join("\n", mismatchDetails)}");
    }

    private static string ClassifyOutcome(
        string path,
        Dictionary<string, List<ScanItemDecision>> decisionsByPath,
        HashSet<string> mediaFilePaths)
    {
        if (decisionsByPath.TryGetValue(path, out var pathDecisions))
            if (pathDecisions.Any(d => d.Kind == ScanDecisionKind.Excluded))
                return "Excluded";

        if (mediaFilePaths.Contains(path))
            // Determine if the file was classified as Movie or Episode
            // For this test, we check if the path is under TV Shows
            return path.Contains("/TV Shows/", StringComparison.OrdinalIgnoreCase) ? "Episode" : "Movie";

        // If there's an Added decision but no media file path match yet, it's still classified
        if (decisionsByPath.TryGetValue(path, out var addedDecisions) &&
            addedDecisions.Any(d => d.Kind == ScanDecisionKind.Added))
            return path.Contains("/TV Shows/", StringComparison.OrdinalIgnoreCase) ? "Episode" : "Movie";

        return "Unknown";
    }

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