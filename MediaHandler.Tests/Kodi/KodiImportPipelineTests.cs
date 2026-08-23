using FluentAssertions;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Common.Models.Kodi;
using MediaHandler.Application.Common.Models.Scanner;
using MediaHandler.Application.Features.KodiImport.DTOs;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Enums;
using MediaHandler.Infrastructure.Kodi;
using MediaHandler.Infrastructure.Nas.Scanner;
using MediaHandler.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MediaHandler.Tests.Kodi;

public class KodiImportPipelineTests
{
    private readonly TestDbContext _context = TestDbContext.Create();
    private readonly IKodiVideoDbReader _reader = Substitute.For<IKodiVideoDbReader>();
    private readonly ITmdbService _tmdb = Substitute.For<ITmdbService>();

    private KodiImportPipeline CreatePipeline()
    {
        var matcher = new TmdbMatcher(_tmdb, NullLogger<TmdbMatcher>.Instance);
        return new KodiImportPipeline(
            _context,
            _reader,
            _tmdb,
            matcher,
            NullLogger<KodiImportPipeline>.Instance);
    }

    private ImportRun CreateRun(KodiImportMode mode = KodiImportMode.Import)
    {
        var run = new ImportRun
        {
            Id = Guid.NewGuid(),
            Mode = mode,
            Status = ImportRunStatus.Running,
            SourceFileName = "MyVideos121.db",
            SchemaVersion = 121,
            StartedAt = DateTime.UtcNow
        };
        _context.ImportRuns.Add(run);
        _context.SaveChanges();
        return run;
    }

    private KodiImportStartParameters Parameters(ImportRun run, params KodiPathMappingSnapshot[] mappings)
    {
        return new KodiImportStartParameters(
            run.Id,
            "/tmp/MyVideos121.db",
            run.SourceFileName,
            run.SchemaVersion,
            run.Mode,
            mappings.ToList());
    }

    private MediaFile SeedFile(string path, Guid? mediaId = null)
    {
        var file = new MediaFile
        {
            FilePath = path,
            MediaId = mediaId,
            FirstSeenScanRunId = Guid.NewGuid()
        };
        _context.MediaFiles.Add(file);
        _context.SaveChanges();
        return file;
    }

    private Media SeedMedia(MediaType type, int tmdbId, string title)
    {
        var media = new Media { Type = type, TmdbId = tmdbId, Title = title };
        _context.Medias.Add(media);
        _context.SaveChanges();
        return media;
    }

    private static KodiLibrarySnapshot SnapshotWithMovie(
        int id = 1,
        string title = "Test Movie",
        int? year = 1999,
        int? tmdbId = 603,
        string? imdbId = null,
        string fileRef = "smb://FREEBOX/Films/Test%20Movie/Test%20Movie.mkv")
    {
        var externalIds = new List<KodiExternalId>();
        if (tmdbId.HasValue)
            externalIds.Add(new KodiExternalId("tmdb", tmdbId.Value.ToString()));
        if (imdbId is not null)
            externalIds.Add(new KodiExternalId("imdb", imdbId));

        return new KodiLibrarySnapshot(
            [new KodiMovieItem(id, title, null, year, externalIds, [fileRef])],
            [],
            []);
    }

    private static KodiLibrarySnapshot SnapshotWithShow(
        int showId = 10,
        string title = "Test Show",
        int? year = 2010,
        int? tmdbId = 1000,
        string? imdbId = null,
        params KodiEpisodeItem[] episodes)
    {
        var externalIds = new List<KodiExternalId>();
        if (tmdbId.HasValue)
            externalIds.Add(new KodiExternalId("tmdb", tmdbId.Value.ToString()));
        if (imdbId is not null)
            externalIds.Add(new KodiExternalId("imdb", imdbId));

        return new KodiLibrarySnapshot(
            [],
            [new KodiShowItem(showId, title, year, externalIds, episodes.ToList())],
            []);
    }

    private static KodiLibrarySnapshot EmptySnapshot() =>
        new([], [], []);

    private static KodiPathMappingSnapshot FilmsMapping() =>
        new("smb://FREEBOX/Films", "/nas/Movies");

    private static KodiPathMappingSnapshot ShowsMapping() =>
        new("smb://FREEBOX/Series", "/nas/Shows");

    // ═══════════════════════════════════════════════════════════════════════════
    // Identity resolution
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Import_MovieWithTmdbId_CreatesMediaWithoutProviderCall()
    {
        var snapshot = SnapshotWithMovie();
        _reader.ReadAsync("/tmp/MyVideos121.db", 121, Arg.Any<CancellationToken>()).Returns(snapshot);

        var run = CreateRun();
        var pipeline = CreatePipeline();

        await pipeline.ExecuteAsync(run, Parameters(run), TestContext.Current.CancellationToken);

        _context.Medias.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Type = MediaType.Film, TmdbId = 603, Title = "Test Movie", Year = 1999 });
        await _tmdb.DidNotReceive().FindByExternalIdAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<MediaType?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _tmdb.DidNotReceive().SearchCandidatesAsync(
            Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<MediaType?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Import_ExistingSameKindAndTmdb_ReusesEntryNeverDuplicates()
    {
        SeedMedia(MediaType.Film, 603, "Existing Movie");
        var snapshot = SnapshotWithMovie();
        _reader.ReadAsync("/tmp/MyVideos121.db", 121, Arg.Any<CancellationToken>()).Returns(snapshot);

        var run = CreateRun();
        await CreatePipeline().ExecuteAsync(run, Parameters(run), TestContext.Current.CancellationToken);

        _context.Medias.Should().HaveCount(1);
        _context.ImportItemOutcomes.Should().ContainSingle()
            .Which.Outcome.Should().Be(ImportItemStatus.Reused);
    }

    [Fact]
    public async Task Import_ImdbOnlyMovie_ResolvesViaFindByExternalId()
    {
        var snapshot = SnapshotWithMovie(tmdbId: null, imdbId: "tt0133093");
        _reader.ReadAsync("/tmp/MyVideos121.db", 121, Arg.Any<CancellationToken>()).Returns(snapshot);
        _tmdb.FindByExternalIdAsync("tt0133093", "imdb_id", MediaType.Film, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new TmdbIdLookupResult(603, MediaType.Film, "The Matrix", 1999, null));

        var run = CreateRun();
        await CreatePipeline().ExecuteAsync(run, Parameters(run), TestContext.Current.CancellationToken);

        _context.Medias.Should().ContainSingle()
            .Which.TmdbId.Should().Be(603);
        await _tmdb.Received().FindByExternalIdAsync("tt0133093", "imdb_id", MediaType.Film, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Import_TitleSearchSingleConfidentMatch_CreatesEntry()
    {
        var snapshot = SnapshotWithMovie(tmdbId: null, title: "The Matrix", year: 1999);
        _reader.ReadAsync("/tmp/MyVideos121.db", 121, Arg.Any<CancellationToken>()).Returns(snapshot);
        _tmdb.SearchCandidatesAsync("The Matrix", 1999, MediaType.Film, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([
                new TmdbSearchCandidate(603, MediaType.Film, "The Matrix", 1999, 100m, null)
            ]);

        var run = CreateRun();
        await CreatePipeline().ExecuteAsync(run, Parameters(run), TestContext.Current.CancellationToken);

        _context.Medias.Should().ContainSingle()
            .Which.TmdbId.Should().Be(603);
        _context.ImportItemOutcomes.Should().ContainSingle()
            .Which.Outcome.Should().Be(ImportItemStatus.Created);
    }

    [Fact]
    public async Task Import_AmbiguousCandidates_NoMediaCreatedReviewItemWithKodiImportSource()
    {
        var fileRef = "smb://FREEBOX/Films/Ambiguous/Ambiguous.mkv";
        var snapshot = SnapshotWithMovie(id: 1, tmdbId: null, title: "Ambiguous", year: 2020, fileRef: fileRef);
        _reader.ReadAsync("/tmp/MyVideos121.db", 121, Arg.Any<CancellationToken>()).Returns(snapshot);
        _tmdb.SearchCandidatesAsync("Ambiguous", 2020, MediaType.Film, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([
                new TmdbSearchCandidate(1, MediaType.Film, "Ambiguous One", 2020, 100m, null),
                new TmdbSearchCandidate(2, MediaType.Film, "Ambiguous Two", 2020, 98m, null)
            ]);

        var run = CreateRun();
        await CreatePipeline().ExecuteAsync(run, Parameters(run), TestContext.Current.CancellationToken);

        _context.Medias.Should().BeEmpty();
        var review = _context.ReviewItems.Should().ContainSingle().Which;
        review.Source.Should().Be(ReviewItemSource.KodiImport);
        review.FilePath.Should().Be(fileRef);
        review.Status.Should().Be(ReviewStatus.Open);
        _context.ImportItemOutcomes.Should().ContainSingle()
            .Which.Outcome.Should().Be(ImportItemStatus.NeedsReview);
    }

    [Fact]
    public async Task Import_ShowWithEpisodes_MaterializesSeasonsEpisodesAtCorrectNumbers()
    {
        var snapshot = SnapshotWithShow(
            episodes:
            [
                new KodiEpisodeItem(100, 1, 1, "Pilot", "smb://FREEBOX/Series/Test%20Show/S01E01.mkv"),
                new KodiEpisodeItem(101, 1, 2, "Episode 2", "smb://FREEBOX/Series/Test%20Show/S01E02.mkv"),
                new KodiEpisodeItem(102, 2, 1, "Season 2 Pilot", "smb://FREEBOX/Series/Test%20Show/S02E01.mkv")
            ]);
        _reader.ReadAsync("/tmp/MyVideos121.db", 121, Arg.Any<CancellationToken>()).Returns(snapshot);

        var run = CreateRun();
        await CreatePipeline().ExecuteAsync(run, Parameters(run, ShowsMapping()), TestContext.Current.CancellationToken);

        var show = _context.Medias.Should().ContainSingle().Which;
        show.Type.Should().Be(MediaType.TvShow);
        _context.TvSeasons.Should().HaveCount(2);
        _context.TvEpisodes.Should().HaveCount(3);
        _context.ImportItemOutcomes.Should().Contain(o => o.KodiItemKind == KodiItemKind.TvShow);
        _context.ImportItemOutcomes.Where(o => o.KodiItemKind == KodiItemKind.Episode).Should().HaveCount(3);
    }

    [Fact]
    public async Task Import_ZeroEpisodeShow_CreatesEmptyShow()
    {
        var snapshot = SnapshotWithShow();
        _reader.ReadAsync("/tmp/MyVideos121.db", 121, Arg.Any<CancellationToken>()).Returns(snapshot);

        var run = CreateRun();
        await CreatePipeline().ExecuteAsync(run, Parameters(run), TestContext.Current.CancellationToken);

        _context.Medias.Should().ContainSingle();
        _context.TvSeasons.Should().BeEmpty();
        _context.TvEpisodes.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Linking
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Import_MappedMovieFile_LinksScannedFile()
    {
        var snapshot = SnapshotWithMovie(fileRef: "smb://FREEBOX/Films/Matrix/Matrix.mkv");
        _reader.ReadAsync("/tmp/MyVideos121.db", 121, Arg.Any<CancellationToken>()).Returns(snapshot);
        var file = SeedFile("/nas/Movies/Matrix/Matrix.mkv");

        var run = CreateRun();
        await CreatePipeline().ExecuteAsync(run, Parameters(run, FilmsMapping()), TestContext.Current.CancellationToken);

        var media = _context.Medias.Should().ContainSingle().Which;
        file = _context.MediaFiles.Should().ContainSingle().Which;
        file.MediaId.Should().Be(media.Id);
        _context.ImportItemOutcomes.Should().ContainSingle()
            .Which.LinkOutcome.Should().Be(ImportLinkStatus.Linked);
    }

    [Fact]
    public async Task Import_EpisodeFile_CreatesEpisodeLinkAndAssociatesShow()
    {
        var snapshot = SnapshotWithShow(
            episodes: [new KodiEpisodeItem(100, 1, 1, "Pilot", "smb://FREEBOX/Series/Show/S01E01.mkv")]);
        _reader.ReadAsync("/tmp/MyVideos121.db", 121, Arg.Any<CancellationToken>()).Returns(snapshot);
        var file = SeedFile("/nas/Shows/Show/S01E01.mkv");

        var run = CreateRun();
        await CreatePipeline().ExecuteAsync(run, Parameters(run, ShowsMapping()), TestContext.Current.CancellationToken);

        var show = _context.Medias.Should().ContainSingle().Which;
        file = _context.MediaFiles.Should().ContainSingle().Which;
        file.MediaId.Should().Be(show.Id);
        _context.EpisodeFileLinks.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { TvEpisodeId = _context.TvEpisodes.Single().Id, MediaFileId = file.Id, OrderInFile = 1 });
    }

    [Fact]
    public async Task Import_UnmappedPrefix_EntryCreatedUnlinkedPrefixReported()
    {
        var snapshot = SnapshotWithMovie(fileRef: "smb://UNKNOWN/Movies/X/X.mkv");
        _reader.ReadAsync("/tmp/MyVideos121.db", 121, Arg.Any<CancellationToken>()).Returns(snapshot);

        var run = CreateRun();
        await CreatePipeline().ExecuteAsync(run, Parameters(run, FilmsMapping()), TestContext.Current.CancellationToken);

        _context.Medias.Should().ContainSingle();
        _context.MediaFiles.Should().BeEmpty();
        var outcome = _context.ImportItemOutcomes.Should().ContainSingle().Which;
        outcome.LinkOutcome.Should().Be(ImportLinkStatus.UnmatchedPath);
        outcome.KodiPathPrefix.Should().Be("smb://UNKNOWN/Movies/X");
        run = _context.ImportRuns.Single();
        ImportRunMappings.ToDetailDto(run).UnmatchedPrefixes.Should().Contain("smb://UNKNOWN/Movies/X");
    }

    [Fact]
    public async Task Import_MappedButNotScanned_ReportedNoScannedFile_LinkedAfterRescan()
    {
        var fileRef = "smb://FREEBOX/Films/Later/Later.mkv";
        var snapshot = SnapshotWithMovie(id: 1, fileRef: fileRef);
        _reader.ReadAsync("/tmp/MyVideos121.db", 121, Arg.Any<CancellationToken>()).Returns(snapshot);

        var run = CreateRun();
        await CreatePipeline().ExecuteAsync(run, Parameters(run, FilmsMapping()), TestContext.Current.CancellationToken);

        _context.ImportItemOutcomes.Should().ContainSingle()
            .Which.LinkOutcome.Should().Be(ImportLinkStatus.NoScannedFile);

        // Simulate a scan discovering the file, then re-import.
        var file = SeedFile("/nas/Movies/Later/Later.mkv");
        var run2 = CreateRun();
        await CreatePipeline().ExecuteAsync(run2, Parameters(run2, FilmsMapping()), TestContext.Current.CancellationToken);

        file = _context.MediaFiles.Single();
        file.MediaId.Should().Be(_context.Medias.Single().Id);
    }

    [Fact]
    public async Task Import_StackedMovieAllPartsScanned_LinksAllPartsUnderOneStackGroup()
    {
        var snapshot = new KodiLibrarySnapshot(
            [new KodiMovieItem(1, "Stacked", null, 2000, [new KodiExternalId("tmdb", "500")],
                [
                    "smb://FREEBOX/Films/Stacked/Stacked%20cd1.mkv",
                    "smb://FREEBOX/Films/Stacked/Stacked%20cd2.mkv"
                ])],
            [],
            []);
        _reader.ReadAsync("/tmp/MyVideos121.db", 121, Arg.Any<CancellationToken>()).Returns(snapshot);
        var part1 = SeedFile("/nas/Movies/Stacked/Stacked cd1.mkv");
        var part2 = SeedFile("/nas/Movies/Stacked/Stacked cd2.mkv");

        var run = CreateRun();
        await CreatePipeline().ExecuteAsync(run, Parameters(run, FilmsMapping()), TestContext.Current.CancellationToken);

        var media = _context.Medias.Single();
        part1 = _context.MediaFiles.First(f => f.FilePath == part1.FilePath);
        part2 = _context.MediaFiles.First(f => f.FilePath == part2.FilePath);
        part1.MediaId.Should().Be(media.Id);
        part2.MediaId.Should().Be(media.Id);
        part1.StackGroupId.Should().NotBeNull();
        part1.StackGroupId.Should().Be(part2.StackGroupId);
        part1.Role.Should().Be(MediaFileRole.Main);
        part2.Role.Should().Be(MediaFileRole.StackedPart);
    }

    [Fact]
    public async Task Import_StackedMovieOnePartScanned_PartiallyLinkedMissingPartReported()
    {
        var snapshot = new KodiLibrarySnapshot(
            [new KodiMovieItem(1, "Stacked", null, 2000, [new KodiExternalId("tmdb", "500")],
                [
                    "smb://FREEBOX/Films/Stacked/Stacked%20cd1.mkv",
                    "smb://FREEBOX/Films/Stacked/Stacked%20cd2.mkv"
                ])],
            [],
            []);
        _reader.ReadAsync("/tmp/MyVideos121.db", 121, Arg.Any<CancellationToken>()).Returns(snapshot);
        var part1 = SeedFile("/nas/Movies/Stacked/Stacked cd1.mkv");

        var run = CreateRun();
        await CreatePipeline().ExecuteAsync(run, Parameters(run, FilmsMapping()), TestContext.Current.CancellationToken);

        var outcome = _context.ImportItemOutcomes.Should().ContainSingle().Which;
        outcome.LinkOutcome.Should().Be(ImportLinkStatus.PartiallyLinked);
        outcome.LinkedFileCount.Should().Be(1);
        part1 = _context.MediaFiles.Single();
        part1.MediaId.Should().NotBeNull();
    }

    [Fact]
    public async Task Import_MultiEpisodeFile_EachEpisodeLinkedWithPosition()
    {
        var fileRef = "smb://FREEBOX/Series/Show/S01E01-E02.mkv";
        var snapshot = SnapshotWithShow(
            episodes:
            [
                new KodiEpisodeItem(100, 1, 1, "E1", fileRef),
                new KodiEpisodeItem(101, 1, 2, "E2", fileRef)
            ]);
        _reader.ReadAsync("/tmp/MyVideos121.db", 121, Arg.Any<CancellationToken>()).Returns(snapshot);
        var file = SeedFile("/nas/Shows/Show/S01E01-E02.mkv");

        var run = CreateRun();
        await CreatePipeline().ExecuteAsync(run, Parameters(run, ShowsMapping()), TestContext.Current.CancellationToken);

        var links = _context.EpisodeFileLinks.OrderBy(l => l.OrderInFile).ToList();
        links.Should().HaveCount(2);
        links[0].OrderInFile.Should().Be(1);
        links[1].OrderInFile.Should().Be(2);
        links.Select(l => l.MediaFileId).Distinct().Should().ContainSingle().Which.Should().Be(file.Id);
    }

    [Fact]
    public async Task Import_FileLinkedToDifferentMedia_PreservesLinkReportsConflict()
    {
        var other = SeedMedia(MediaType.Film, 999, "Other Movie");
        var file = SeedFile("/nas/Movies/Matrix/Matrix.mkv", other.Id);

        var snapshot = SnapshotWithMovie(fileRef: "smb://FREEBOX/Films/Matrix/Matrix.mkv");
        _reader.ReadAsync("/tmp/MyVideos121.db", 121, Arg.Any<CancellationToken>()).Returns(snapshot);

        var run = CreateRun();
        await CreatePipeline().ExecuteAsync(run, Parameters(run, FilmsMapping()), TestContext.Current.CancellationToken);

        file = _context.MediaFiles.Single();
        file.MediaId.Should().Be(other.Id);
        _context.ImportItemOutcomes.Should().ContainSingle()
            .Which.Outcome.Should().Be(ImportItemStatus.Conflict);
    }

    [Fact]
    public async Task Import_NormalizedPaths_MatchScannedFiles()
    {
        var snapshot = SnapshotWithMovie(fileRef: "smb://FREEBOX/Films/Matrix\\Matrix.mkv");
        _reader.ReadAsync("/tmp/MyVideos121.db", 121, Arg.Any<CancellationToken>()).Returns(snapshot);
        var file = SeedFile("/nas/Movies/Matrix/Matrix.mkv");

        var run = CreateRun();
        await CreatePipeline().ExecuteAsync(run, Parameters(run, FilmsMapping()), TestContext.Current.CancellationToken);

        file = _context.MediaFiles.Single();
        file.MediaId.Should().NotBeNull();
    }

    [Fact]
    public async Task Import_MissingMarkedFile_StillLinks()
    {
        var snapshot = SnapshotWithMovie(fileRef: "smb://FREEBOX/Films/Matrix/Matrix.mkv");
        _reader.ReadAsync("/tmp/MyVideos121.db", 121, Arg.Any<CancellationToken>()).Returns(snapshot);
        var file = SeedFile("/nas/Movies/Matrix/Matrix.mkv");
        file.MissingSince = DateTime.UtcNow;
        _context.SaveChanges();

        var run = CreateRun();
        await CreatePipeline().ExecuteAsync(run, Parameters(run, FilmsMapping()), TestContext.Current.CancellationToken);

        file = _context.MediaFiles.Single();
        file.MediaId.Should().NotBeNull();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Idempotency / re-import
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Import_IdenticalSecondRun_AllUnchangedZeroWrites()
    {
        var fileRef = "smb://FREEBOX/Films/Matrix/Matrix.mkv";
        var snapshot = SnapshotWithMovie(fileRef: fileRef);
        _reader.ReadAsync("/tmp/MyVideos121.db", 121, Arg.Any<CancellationToken>()).Returns(snapshot);
        var file = SeedFile("/nas/Movies/Matrix/Matrix.mkv");

        var run1 = CreateRun();
        await CreatePipeline().ExecuteAsync(run1, Parameters(run1, FilmsMapping()), TestContext.Current.CancellationToken);

        // Ensure first run is the baseline (Completed + Import)
        run1.Status = ImportRunStatus.Completed;
        run1.FinishedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var mediaCount = _context.Medias.Count();
        var episodeCount = _context.TvEpisodes.Count();

        var run2 = CreateRun();
        await CreatePipeline().ExecuteAsync(run2, Parameters(run2, FilmsMapping()), TestContext.Current.CancellationToken);

        _context.Medias.Should().HaveCount(mediaCount);
        _context.TvEpisodes.Should().HaveCount(episodeCount);
        var outcome = _context.ImportItemOutcomes.Where(o => o.ImportRunId == run2.Id).Should().ContainSingle().Which;
        outcome.Outcome.Should().Be(ImportItemStatus.Unchanged);
        outcome.LinkOutcome.Should().Be(ImportLinkStatus.AlreadyLinked);
    }

    [Fact]
    public async Task Import_ReuploadWithNewItems_OnlyNewCreated()
    {
        var fileRef = "smb://FREEBOX/Films/Matrix/Matrix.mkv";
        var first = SnapshotWithMovie(id: 1, tmdbId: 603, fileRef: fileRef);
        _reader.ReadAsync("/tmp/MyVideos121.db", 121, Arg.Any<CancellationToken>()).Returns(first);
        SeedFile("/nas/Movies/Matrix/Matrix.mkv");

        var run1 = CreateRun();
        await CreatePipeline().ExecuteAsync(run1, Parameters(run1, FilmsMapping()), TestContext.Current.CancellationToken);
        run1.Status = ImportRunStatus.Completed;
        run1.FinishedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var second = new KodiLibrarySnapshot(
            [
                new KodiMovieItem(1, "The Matrix", null, 1999, [new KodiExternalId("tmdb", "603")],
                    [fileRef]),
                new KodiMovieItem(2, "New Movie", null, 2020, [new KodiExternalId("tmdb", "700")],
                    ["smb://FREEBOX/Films/New/New.mkv"])
            ],
            [],
            []);
        _reader.ReadAsync("/tmp/MyVideos121.db", 121, Arg.Any<CancellationToken>()).Returns(second);
        SeedFile("/nas/Movies/New/New.mkv");

        var run2 = CreateRun();
        await CreatePipeline().ExecuteAsync(run2, Parameters(run2, FilmsMapping()), TestContext.Current.CancellationToken);

        _context.Medias.Should().HaveCount(2);
        var newOutcome = _context.ImportItemOutcomes.Where(o => o.ImportRunId == run2.Id && o.KodiItemId == 2)
            .Should().ContainSingle().Which;
        newOutcome.Outcome.Should().Be(ImportItemStatus.Created);
    }

    [Fact]
    public async Task Import_ReidentifiedItemWithLinkedFile_ConflictNoDuplicate()
    {
        var oldMedia = SeedMedia(MediaType.Film, 100, "Old Identity");
        var file = SeedFile("/nas/Movies/Matrix/Matrix.mkv", oldMedia.Id);

        var snapshot = SnapshotWithMovie(tmdbId: 603, fileRef: "smb://FREEBOX/Films/Matrix/Matrix.mkv");
        _reader.ReadAsync("/tmp/MyVideos121.db", 121, Arg.Any<CancellationToken>()).Returns(snapshot);

        var run = CreateRun();
        await CreatePipeline().ExecuteAsync(run, Parameters(run, FilmsMapping()), TestContext.Current.CancellationToken);

        file = _context.MediaFiles.Single();
        file.MediaId.Should().Be(oldMedia.Id);
        _context.Medias.Should().HaveCount(1);
        _context.ImportItemOutcomes.Should().ContainSingle()
            .Which.Outcome.Should().Be(ImportItemStatus.Conflict);
    }

    [Fact]
    public async Task Import_ItemRemovedFromKodi_LeftUntouchedAndReported()
    {
        var fileRef = "smb://FREEBOX/Films/Matrix/Matrix.mkv";
        var first = SnapshotWithMovie(id: 1, tmdbId: 603, fileRef: fileRef);
        _reader.ReadAsync("/tmp/MyVideos121.db", 121, Arg.Any<CancellationToken>()).Returns(first);
        SeedFile("/nas/Movies/Matrix/Matrix.mkv");

        var run1 = CreateRun();
        await CreatePipeline().ExecuteAsync(run1, Parameters(run1, FilmsMapping()), TestContext.Current.CancellationToken);
        run1.Status = ImportRunStatus.Completed;
        run1.FinishedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Second upload is empty.
        _reader.ReadAsync("/tmp/MyVideos121.db", 121, Arg.Any<CancellationToken>()).Returns(EmptySnapshot());

        var run2 = CreateRun();
        await CreatePipeline().ExecuteAsync(run2, Parameters(run2), TestContext.Current.CancellationToken);

        _context.Medias.Should().ContainSingle();
        _context.ImportItemOutcomes.Where(o => o.ImportRunId == run2.Id).Should().ContainSingle()
            .Which.Outcome.Should().Be(ImportItemStatus.NoLongerInKodi);
    }

    [Fact]
    public async Task Import_MusicVideos_SkippedAndCounted()
    {
        var snapshot = new KodiLibrarySnapshot(
            [],
            [],
            [new KodiMusicVideoItem(1, "Music Video One"), new KodiMusicVideoItem(2, "Music Video Two")]);
        _reader.ReadAsync("/tmp/MyVideos121.db", 121, Arg.Any<CancellationToken>()).Returns(snapshot);

        var run = CreateRun();
        await CreatePipeline().ExecuteAsync(run, Parameters(run), TestContext.Current.CancellationToken);

        _context.Medias.Should().BeEmpty();
        _context.ImportItemOutcomes.Should().HaveCount(2);
        _context.ImportItemOutcomes.Should().OnlyContain(o => o.Outcome == ImportItemStatus.SkippedMusicVideo);
        run.SkippedMusicVideos.Should().Be(2);
    }

    [Fact]
    public async Task Import_DuplicateTmdbWithinKodi_SingleEntryBothFilesLinkedInformational()
    {
        var snapshot = new KodiLibrarySnapshot(
            [
                new KodiMovieItem(1, "Edition A", null, 1999, [new KodiExternalId("tmdb", "603")],
                    ["smb://FREEBOX/Films/A/A.mkv"]),
                new KodiMovieItem(2, "Edition B", null, 1999, [new KodiExternalId("tmdb", "603")],
                    ["smb://FREEBOX/Films/B/B.mkv"])
            ],
            [],
            []);
        _reader.ReadAsync("/tmp/MyVideos121.db", 121, Arg.Any<CancellationToken>()).Returns(snapshot);
        SeedFile("/nas/Movies/A/A.mkv");
        SeedFile("/nas/Movies/B/B.mkv");

        var run = CreateRun();
        await CreatePipeline().ExecuteAsync(run, Parameters(run, FilmsMapping()), TestContext.Current.CancellationToken);

        _context.Medias.Should().HaveCount(1);
        _context.MediaFiles.Where(f => f.MediaId.HasValue).Should().HaveCount(2);
        _context.ImportItemOutcomes.Should().Contain(o => !string.IsNullOrEmpty(o.Reason) && o.Reason.Contains("Duplicate TMDB identity"));
    }

    [Fact]
    public async Task Import_SameFileAsMovieAndEpisode_FirstLinkWinsConflictReported()
    {
        var fileRef = "smb://FREEBOX/Series/Show/MovieAsEpisode.mkv";
        var movieSnapshot = new KodiLibrarySnapshot(
            [new KodiMovieItem(1, "Movie", null, 1999, [new KodiExternalId("tmdb", "603")], [fileRef])],
            [],
            []);
        _reader.ReadAsync("/tmp/MyVideos121.db", 121, Arg.Any<CancellationToken>()).Returns(movieSnapshot);
        var file = SeedFile("/nas/Shows/Show/MovieAsEpisode.mkv");

        var run1 = CreateRun();
        await CreatePipeline().ExecuteAsync(run1, Parameters(run1, ShowsMapping()), TestContext.Current.CancellationToken);
        run1.Status = ImportRunStatus.Completed;
        run1.FinishedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var showSnapshot = SnapshotWithShow(
            tmdbId: 1000,
            episodes: [new KodiEpisodeItem(100, 1, 1, "Pilot", fileRef)]);
        _reader.ReadAsync("/tmp/MyVideos121.db", 121, Arg.Any<CancellationToken>()).Returns(showSnapshot);

        var run2 = CreateRun();
        await CreatePipeline().ExecuteAsync(run2, Parameters(run2, ShowsMapping()), TestContext.Current.CancellationToken);

        file = _context.MediaFiles.Single();
        file.MediaId.Should().Be(_context.Medias.First(m => m.Type == MediaType.Film).Id);
        _context.ImportItemOutcomes.Where(o => o.ImportRunId == run2.Id)
            .Should().Contain(o => o.KodiItemKind == KodiItemKind.Episode && o.Outcome == ImportItemStatus.Conflict);
    }

    [Fact]
    public async Task Import_ProviderOutage_LookupItemsFailedOthersImportedRunCompletes()
    {
        var direct = SnapshotWithMovie(id: 1, tmdbId: 603, fileRef: "smb://FREEBOX/Films/Direct/Direct.mkv");
        var lookup = SnapshotWithMovie(id: 2, tmdbId: null, imdbId: "tt9999999", title: "Lookup", fileRef: "smb://FREEBOX/Films/Lookup/Lookup.mkv");
        var combined = new KodiLibrarySnapshot(
            direct.Movies.Concat(lookup.Movies).ToList(),
            [],
            []);
        _reader.ReadAsync("/tmp/MyVideos121.db", 121, Arg.Any<CancellationToken>()).Returns(combined);
        _tmdb.FindByExternalIdAsync("tt9999999", "imdb_id", MediaType.Film, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<TmdbIdLookupResult?>>(_ => throw new HttpRequestException("TMDB down"));

        var run = CreateRun();
        await CreatePipeline().ExecuteAsync(run, Parameters(run), TestContext.Current.CancellationToken);

        _context.Medias.Should().HaveCount(1);
        _context.ImportItemOutcomes.Should().HaveCount(2);
        _context.ImportItemOutcomes.Should().Contain(o => o.Outcome == ImportItemStatus.Created);
        _context.ImportItemOutcomes.Should().Contain(o => o.Outcome == ImportItemStatus.IdentityLookupFailed);
        run.Status.Should().Be(ImportRunStatus.Running); // direct ExecuteAsync does not transition status
    }

    [Fact]
    public async Task Import_AdminResolvedReviewItem_ResolutionReusedOnNextImport()
    {
        var fileRef = "smb://FREEBOX/Films/Ambiguous/Ambiguous.mkv";
        var first = SnapshotWithMovie(id: 1, tmdbId: null, title: "Ambiguous", year: 2020, fileRef: fileRef);
        _reader.ReadAsync("/tmp/MyVideos121.db", 121, Arg.Any<CancellationToken>()).Returns(first);
        _tmdb.SearchCandidatesAsync("Ambiguous", 2020, MediaType.Film, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([
                new TmdbSearchCandidate(1, MediaType.Film, "One", 2020, 100m, null),
                new TmdbSearchCandidate(2, MediaType.Film, "Two", 2020, 98m, null)
            ]);

        var run1 = CreateRun();
        await CreatePipeline().ExecuteAsync(run1, Parameters(run1), TestContext.Current.CancellationToken);
        run1.Status = ImportRunStatus.Completed;
        run1.FinishedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var review = _context.ReviewItems.Single();
        review.Status = ReviewStatus.Resolved;
        review.ResolvedTmdbId = 42;
        review.ResolvedKind = MediaType.Film;
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Second upload of the same item, now with an admin resolution in the DB.
        _reader.ReadAsync("/tmp/MyVideos121.db", 121, Arg.Any<CancellationToken>()).Returns(first);
        var run2 = CreateRun();
        await CreatePipeline().ExecuteAsync(run2, Parameters(run2), TestContext.Current.CancellationToken);

        _context.Medias.Should().ContainSingle()
            .Which.TmdbId.Should().Be(42);
    }

    [Fact]
    public async Task Import_SeasonZeroEpisode_MaterializedAndLinked()
    {
        var fileRef = "smb://FREEBOX/Series/Show/S00E01.mkv";
        var snapshot = SnapshotWithShow(
            episodes: [new KodiEpisodeItem(100, 0, 1, "Special", fileRef)]);
        _reader.ReadAsync("/tmp/MyVideos121.db", 121, Arg.Any<CancellationToken>()).Returns(snapshot);
        SeedFile("/nas/Shows/Show/S00E01.mkv");

        var run = CreateRun();
        await CreatePipeline().ExecuteAsync(run, Parameters(run, ShowsMapping()), TestContext.Current.CancellationToken);

        _context.TvSeasons.Should().ContainSingle()
            .Which.SeasonNumber.Should().Be(0);
        _context.TvEpisodes.Should().ContainSingle()
            .Which.EpisodeNumber.Should().Be(1);
    }

    [Fact]
    public async Task Preview_ValidSnapshot_PersistsOnlyRunAndOutcomeRows()
    {
        var snapshot = SnapshotWithMovie();
        _reader.ReadAsync("/tmp/MyVideos121.db", 121, Arg.Any<CancellationToken>()).Returns(snapshot);

        var run = CreateRun(KodiImportMode.Preview);
        await CreatePipeline().ExecuteAsync(run, Parameters(run), TestContext.Current.CancellationToken);

        _context.Medias.Should().BeEmpty();
        _context.TvSeasons.Should().BeEmpty();
        _context.TvEpisodes.Should().BeEmpty();
        _context.MediaFiles.Should().BeEmpty();
        _context.ReviewItems.Should().BeEmpty();
        _context.StackGroups.Should().BeEmpty();
        _context.EpisodeFileLinks.Should().BeEmpty();
        _context.ImportItemOutcomes.Should().ContainSingle();
    }

    [Fact]
    public async Task Preview_ItemWithoutTmdbId_RequiresIdentityLookupZeroProviderCalls()
    {
        var snapshot = SnapshotWithMovie(tmdbId: null, title: "Lookup", year: 2020);
        _reader.ReadAsync("/tmp/MyVideos121.db", 121, Arg.Any<CancellationToken>()).Returns(snapshot);

        var run = CreateRun(KodiImportMode.Preview);
        await CreatePipeline().ExecuteAsync(run, Parameters(run), TestContext.Current.CancellationToken);

        _context.ImportItemOutcomes.Should().ContainSingle()
            .Which.Outcome.Should().Be(ImportItemStatus.RequiresIdentityLookup);
        await _tmdb.DidNotReceive().SearchCandidatesAsync(
            Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<MediaType?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _tmdb.DidNotReceive().FindByExternalIdAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<MediaType?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Preview_ProjectsConflictsAndUnmatchedPrefixes()
    {
        var other = SeedMedia(MediaType.Film, 999, "Other");
        SeedFile("/nas/Movies/Matrix/Matrix.mkv", other.Id);

        var snapshot = SnapshotWithMovie(fileRef: "smb://FREEBOX/Films/Matrix/Matrix.mkv");
        _reader.ReadAsync("/tmp/MyVideos121.db", 121, Arg.Any<CancellationToken>()).Returns(snapshot);

        var run = CreateRun(KodiImportMode.Preview);
        await CreatePipeline().ExecuteAsync(run, Parameters(run, FilmsMapping()), TestContext.Current.CancellationToken);

        _context.ImportItemOutcomes.Should().ContainSingle()
            .Which.Outcome.Should().Be(ImportItemStatus.Conflict);
        _context.Medias.Should().HaveCount(1); // only the pre-existing one
    }

    [Fact]
    public async Task Counters_ReconcileExactlyWithOutcomeRows()
    {
        var snapshot = new KodiLibrarySnapshot(
            [
                new KodiMovieItem(1, "Direct", null, 1999, [new KodiExternalId("tmdb", "603")],
                    ["smb://FREEBOX/Films/Direct/Direct.mkv"]),
                new KodiMovieItem(2, "Unmapped", null, 2000, [new KodiExternalId("tmdb", "604")],
                    ["smb://UNKNOWN/Unmapped/Unmapped.mkv"]),
                new KodiMovieItem(3, "MusicLike", null, 2020, [new KodiExternalId("tmdb", "605")],
                    ["pvr://recordings/MusicLike.mkv"])
            ],
            [],
            [new KodiMusicVideoItem(50, "Music Vid")]);
        _reader.ReadAsync("/tmp/MyVideos121.db", 121, Arg.Any<CancellationToken>()).Returns(snapshot);
        SeedFile("/nas/Movies/Direct/Direct.mkv");

        var run = CreateRun();
        await CreatePipeline().ExecuteAsync(run, Parameters(run, FilmsMapping()), TestContext.Current.CancellationToken);

        var outcomes = _context.ImportItemOutcomes.Where(o => o.ImportRunId == run.Id).ToList();
        run.TotalItems.Should().Be(outcomes.Count(o => o.Outcome != ImportItemStatus.NoLongerInKodi));
        run.FilesLinked.Should().Be(outcomes.Sum(o => o.LinkedFileCount));
        run.UnmatchedPaths.Should().Be(outcomes.Count(o => o.LinkOutcome == ImportLinkStatus.UnmatchedPath));
        run.UnsupportedLocations.Should().Be(outcomes.Count(o => o.LinkOutcome == ImportLinkStatus.UnsupportedLocation));
        run.MoviesCreated.Should().Be(outcomes.Count(o => o.KodiItemKind == KodiItemKind.Movie && o.Outcome == ImportItemStatus.Created));
        run.SkippedMusicVideos.Should().Be(outcomes.Count(o => o.Outcome == ImportItemStatus.SkippedMusicVideo));
    }

    [Fact]
    public async Task Import_ItemRemovedThenReimported_NoLongerInKodiNotReportedAgain()
    {
        var fileRef = "smb://FREEBOX/Films/Matrix/Matrix.mkv";
        var first = SnapshotWithMovie(id: 1, tmdbId: 603, fileRef: fileRef);
        _reader.ReadAsync("/tmp/MyVideos121.db", 121, Arg.Any<CancellationToken>()).Returns(first);
        SeedFile("/nas/Movies/Matrix/Matrix.mkv");

        var run1 = CreateRun();
        await CreatePipeline().ExecuteAsync(run1, Parameters(run1, FilmsMapping()), TestContext.Current.CancellationToken);
        run1.Status = ImportRunStatus.Completed;
        run1.FinishedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Second upload is empty -> reports NoLongerInKodi.
        _reader.ReadAsync("/tmp/MyVideos121.db", 121, Arg.Any<CancellationToken>()).Returns(EmptySnapshot());
        var run2 = CreateRun();
        await CreatePipeline().ExecuteAsync(run2, Parameters(run2), TestContext.Current.CancellationToken);
        run2.Status = ImportRunStatus.Completed;
        run2.FinishedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Third upload is still empty -> baseline must exclude prior NoLongerInKodi rows.
        var run3 = CreateRun();
        await CreatePipeline().ExecuteAsync(run3, Parameters(run3), TestContext.Current.CancellationToken);

        _context.ImportItemOutcomes.Where(o => o.ImportRunId == run3.Id).Should().BeEmpty();
    }

    [Fact]
    public async Task Import_ShowWithLaterEpisodeLinkedToDifferentIdentity_ReportsConflict()
    {
        var otherMedia = SeedMedia(MediaType.TvShow, 2000, "Other Show");
        SeedFile("/nas/Shows/Show/S01E01.mkv");
        var file2 = SeedFile("/nas/Shows/Show/S01E02.mkv", otherMedia.Id);

        var snapshot = SnapshotWithShow(
            showId: 10,
            title: "Show",
            year: 2010,
            tmdbId: 1000,
            episodes:
            [
                new KodiEpisodeItem(100, 1, 1, "E1", "smb://FREEBOX/Series/Show/S01E01.mkv"),
                new KodiEpisodeItem(101, 1, 2, "E2", "smb://FREEBOX/Series/Show/S01E02.mkv")
            ]);
        _reader.ReadAsync("/tmp/MyVideos121.db", 121, Arg.Any<CancellationToken>()).Returns(snapshot);

        var run = CreateRun();
        await CreatePipeline().ExecuteAsync(run, Parameters(run, ShowsMapping()), TestContext.Current.CancellationToken);

        file2 = _context.MediaFiles.Single(f => f.Id == file2.Id);
        file2.MediaId.Should().Be(otherMedia.Id);
        _context.ImportItemOutcomes.Where(o => o.ImportRunId == run.Id && o.KodiItemKind == KodiItemKind.TvShow)
            .Should().ContainSingle().Which.Outcome.Should().Be(ImportItemStatus.Conflict);
        _context.TvSeasons.Should().BeEmpty();
        _context.TvEpisodes.Should().BeEmpty();
    }
}
