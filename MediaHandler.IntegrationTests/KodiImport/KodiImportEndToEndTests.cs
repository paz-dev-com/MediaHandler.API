using FluentAssertions;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Common.Models.Kodi;
using MediaHandler.Application.Common.Models.Scanner;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Enums;
using MediaHandler.Infrastructure.Kodi;
using static MediaHandler.Infrastructure.DependencyInjection;
using MediaHandler.Infrastructure.Nas.Scanner;
using MediaHandler.Infrastructure.Options;
using MediaHandler.Infrastructure.Persistence;
using MediaHandler.IntegrationTests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace MediaHandler.IntegrationTests.KodiImport;

/// <summary>
///     End-to-end Kodi import tests using a real SQL Server container, the real SQLite reader,
///     and the real import pipeline. TMDB traffic is stubbed so tests are deterministic and offline.
/// </summary>
public sealed class KodiImportEndToEndTests : IntegrationTestBase
{
    private readonly KodiImportOptions _options = new()
    {
        SupportedSchemaVersions = [119, 121, 131],
        MaxUploadSizeBytes = 100_000_000
    };

    private ITmdbService _tmdb = null!;
    private KodiImportPipeline _pipeline = null!;
    private KodiVideoDbReader _reader = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        _tmdb = Substitute.For<ITmdbService>();
        var matcher = new TmdbMatcher(_tmdb, NullLogger<TmdbMatcher>.Instance);
        var optionsWrapper = Options.Create(_options);
        _reader = new KodiVideoDbReader(optionsWrapper, NullLogger<KodiVideoDbReader>.Instance);
        _pipeline = new KodiImportPipeline(DbContext, _reader, _tmdb, matcher, NullLogger<KodiImportPipeline>.Instance);
    }

    [Fact]
    public async Task FullImport_FixtureWithMoviesShowStackMultiEpisode_CompletesWithExpectedCounters()
    {
        var path = BuildFullFixture();
        try
        {
            SeedFullFixtureFiles();

            var (run, parameters) = StartImportRun(path, KodiImportMode.Import,
                new KodiPathMappingSnapshot("smb://FREEBOX/Films", "/nas/Movies"),
                new KodiPathMappingSnapshot("smb://FREEBOX/Series", "/nas/Shows"));

            await _pipeline.ExecuteAsync(run, parameters, TestContext.Current.CancellationToken);

            run.Status = ImportRunStatus.Completed;
            run.FinishedAt = DateTime.UtcNow;
            await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

            run.MoviesCreated.Should().Be(2);
            run.ShowsCreated.Should().Be(1);
            run.EpisodesCreated.Should().Be(4);
            run.FilesLinked.Should().Be(7, "movie + 2 stack parts + 4 episode links");
            run.UnmatchedPaths.Should().Be(0);
            run.Conflicts.Should().Be(0);
            run.SkippedMusicVideos.Should().Be(1);

            var media = await DbContext.Medias.ToListAsync(TestContext.Current.CancellationToken);
            media.Should().HaveCount(3);
            media.Should().Contain(m => m.Type == MediaType.Film && m.TmdbId == 603);
            media.Should().Contain(m => m.Type == MediaType.Film && m.TmdbId == 700);
            media.Should().Contain(m => m.Type == MediaType.TvShow && m.TmdbId == 1000);
        }
        finally
        {
            KodiTestDbBuilder.Delete(path);
        }
    }

    [Fact]
    public async Task Reimport_SameFixture_AllUnchanged()
    {
        var path = BuildFullFixture();
        try
        {
            var mappings = new[]
            {
                new KodiPathMappingSnapshot("smb://FREEBOX/Films", "/nas/Movies"),
                new KodiPathMappingSnapshot("smb://FREEBOX/Series", "/nas/Shows")
            };

            SeedFullFixtureFiles();

            var (run1, parameters1) = StartImportRun(path, KodiImportMode.Import, mappings);
            await _pipeline.ExecuteAsync(run1, parameters1, TestContext.Current.CancellationToken);
            run1.Status = ImportRunStatus.Completed;
            run1.FinishedAt = DateTime.UtcNow;
            await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

            var mediaCount = await DbContext.Medias.CountAsync(TestContext.Current.CancellationToken);
            var episodeCount = await DbContext.TvEpisodes.CountAsync(TestContext.Current.CancellationToken);

            var (run2, parameters2) = StartImportRun(path, KodiImportMode.Import, mappings);
            await _pipeline.ExecuteAsync(run2, parameters2, TestContext.Current.CancellationToken);

            (await DbContext.Medias.CountAsync(TestContext.Current.CancellationToken)).Should().Be(mediaCount);
            (await DbContext.TvEpisodes.CountAsync(TestContext.Current.CancellationToken)).Should().Be(episodeCount);
            run2.ItemsUnchanged.Should().Be(run2.TotalItems - run2.SkippedMusicVideos,
                "music videos are excluded from the seen-before baseline");
            run2.FilesLinked.Should().Be(0);
        }
        finally
        {
            KodiTestDbBuilder.Delete(path);
        }
    }

    [Fact]
    public async Task Reimport_UpdatedFixture_AddRemoveReidentify_ConvergesAndReports()
    {
        var path1 = BuildInitialFixture();
        try
        {
            SeedInitialFixtureFiles();

            var mappings = new[] { new KodiPathMappingSnapshot("smb://FREEBOX/Films", "/nas/Movies") };
            var (run1, parameters1) = StartImportRun(path1, KodiImportMode.Import, mappings);
            await _pipeline.ExecuteAsync(run1, parameters1, TestContext.Current.CancellationToken);
            run1.Status = ImportRunStatus.Completed;
            run1.FinishedAt = DateTime.UtcNow;
            await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

            var path2 = BuildUpdatedFixture();
            try
            {
                var (run2, parameters2) = StartImportRun(path2, KodiImportMode.Import, mappings);
                await _pipeline.ExecuteAsync(run2, parameters2, TestContext.Current.CancellationToken);

                run2.MoviesCreated.Should().Be(1, "one new movie was added");
                run2.NoLongerInKodi.Should().Be(1, "one movie was removed");
                run2.Conflicts.Should().Be(1, "one movie was re-identified");
            }
            finally
            {
                KodiTestDbBuilder.Delete(path2);
            }
        }
        finally
        {
            KodiTestDbBuilder.Delete(path1);
        }
    }

    [Fact]
    public async Task PreviewThenImport_OutcomesMatchForDirectIdItems()
    {
        var path = BuildDirectIdFixture();
        try
        {
            SeedScannedFile("/nas/Movies/Direct/Direct Movie.mkv");

            var mappings = new[] { new KodiPathMappingSnapshot("smb://FREEBOX/Films", "/nas/Movies") };

            var (previewRun, previewParameters) = StartImportRun(path, KodiImportMode.Preview, mappings);
            await _pipeline.ExecuteAsync(previewRun, previewParameters, TestContext.Current.CancellationToken);
            previewRun.Status = ImportRunStatus.Completed;
            previewRun.FinishedAt = DateTime.UtcNow;
            await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

            previewRun.MoviesCreated.Should().Be(1);
            previewRun.FilesLinked.Should().Be(1);
            (await DbContext.Medias.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);

            var (importRun, importParameters) = StartImportRun(path, KodiImportMode.Import, mappings);
            await _pipeline.ExecuteAsync(importRun, importParameters, TestContext.Current.CancellationToken);

            importRun.MoviesCreated.Should().Be(previewRun.MoviesCreated);
            importRun.FilesLinked.Should().Be(previewRun.FilesLinked);
            (await DbContext.Medias.CountAsync(TestContext.Current.CancellationToken)).Should().Be(1);
        }
        finally
        {
            KodiTestDbBuilder.Delete(path);
        }
    }

    [Fact]
    public async Task StartupRecovery_StuckRunningRun_MarkedFailedAndFilePurged()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"kodi-import-recovery-{Guid.NewGuid()}.db");
        File.WriteAllText(tempFile, "orphan");

        var run = new ImportRun
        {
            Id = Guid.NewGuid(),
            Mode = KodiImportMode.Import,
            Status = ImportRunStatus.Running,
            SourceFileName = "MyVideos121.db",
            SchemaVersion = 121,
            StartedAt = DateTime.UtcNow,
            UploadedFilePath = tempFile
        };
        DbContext.ImportRuns.Add(run);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var services = new ServiceCollection()
            .AddSingleton<MediaHandlerDbContext>(_ => DbContext)
            .AddSingleton<IKodiImportFileStore>(new FakeFileStoreForRecovery())
            .AddLogging()
            .BuildServiceProvider();

        await ApplyImportRunRecoveryAsync(services);

        run = await DbContext.ImportRuns.SingleAsync(TestContext.Current.CancellationToken);
        run.Status.Should().Be(ImportRunStatus.Failed);
        run.UploadedFilePath.Should().BeNull();
        File.Exists(tempFile).Should().BeFalse();
    }

    private (ImportRun Run, KodiImportStartParameters Parameters) StartImportRun(
        string path,
        KodiImportMode mode,
        params KodiPathMappingSnapshot[] mappings)
    {
        var run = new ImportRun
        {
            Id = Guid.NewGuid(),
            Mode = mode,
            Status = ImportRunStatus.Running,
            SourceFileName = Path.GetFileName(path),
            SchemaVersion = 121,
            StartedAt = DateTime.UtcNow,
            UploadedFilePath = path,
            PathMappingsJson = System.Text.Json.JsonSerializer.Serialize(mappings)
        };
        DbContext.ImportRuns.Add(run);
        DbContext.SaveChanges();

        var parameters = new KodiImportStartParameters(
            run.Id,
            path,
            run.SourceFileName,
            run.SchemaVersion,
            mode,
            mappings.ToList());

        return (run, parameters);
    }

    private MediaFile SeedScannedFile(string path, Guid? mediaId = null)
    {
        var file = new MediaFile
        {
            FilePath = path,
            MediaId = mediaId,
            FirstSeenScanRunId = Guid.NewGuid()
        };
        DbContext.MediaFiles.Add(file);
        DbContext.SaveChanges();
        return file;
    }

    private void SeedFullFixtureFiles()
    {
        SeedScannedFile("/nas/Movies/Matrix/The Matrix.mkv");
        SeedScannedFile("/nas/Movies/Stacked/Stacked cd1.mkv");
        SeedScannedFile("/nas/Movies/Stacked/Stacked cd2.mkv");
        SeedScannedFile("/nas/Shows/Show/S01E01.mkv");
        SeedScannedFile("/nas/Shows/Show/S01E02.mkv");
        SeedScannedFile("/nas/Shows/Show/S01E03-E04.mkv");
    }

    private static string BuildFullFixture()
    {
        return KodiTestDbBuilder.CreateVideoDb(
            movies:
            [
                new TestKodiMovie(1, "The Matrix", Year: 1999, Directory: "smb://FREEBOX/Films/Matrix/",
                    FileName: "The Matrix.mkv"),
                new TestKodiMovie(2, "Stacked Movie", Year: 2000, Directory: "smb://FREEBOX/Films/Stacked/",
                    FileName: "stack://smb://FREEBOX/Films/Stacked/Stacked%20cd1.mkv , smb://FREEBOX/Films/Stacked/Stacked%20cd2.mkv")
            ],
            shows:
            [
                new TestKodiShow(10, "Test Show", "2010-01-01",
                [
                    new TestKodiEpisode(100, 1, 1, "Pilot", "smb://FREEBOX/Series/Show/", "S01E01.mkv"),
                    new TestKodiEpisode(101, 1, 2, "E2", "smb://FREEBOX/Series/Show/", "S01E02.mkv"),
                    new TestKodiEpisode(102, 1, 3, "E3", "smb://FREEBOX/Series/Show/", "S01E03-E04.mkv"),
                    new TestKodiEpisode(103, 1, 4, "E4", "smb://FREEBOX/Series/Show/", "S01E03-E04.mkv")
                ])
            ],
            musicVideos: [new TestKodiMusicVideo(50, "Music Vid")],
            uniqueIds:
            [
                new TestKodiUniqueId(1, "movie", "tmdb", "603"),
                new TestKodiUniqueId(2, "movie", "tmdb", "700"),
                new TestKodiUniqueId(10, "tvshow", "tmdb", "1000")
            ]);
    }

    private static string BuildDirectIdFixture()
    {
        return KodiTestDbBuilder.CreateVideoDb(
            movies:
            [
                new TestKodiMovie(1, "Direct Movie", Year: 2020, Directory: "smb://FREEBOX/Films/Direct/",
                    FileName: "Direct Movie.mkv")
            ],
            uniqueIds: [new TestKodiUniqueId(1, "movie", "tmdb", "12345")]);
    }

    private void SeedInitialFixtureFiles()
    {
        SeedScannedFile("/nas/Movies/Kept/Kept.mkv");
        SeedScannedFile("/nas/Movies/Removed/Removed.mkv");
        SeedScannedFile("/nas/Movies/Reidentified/Reidentified.mkv");
    }

    private static string BuildInitialFixture()
    {
        return KodiTestDbBuilder.CreateVideoDb(
            movies:
            [
                new TestKodiMovie(1, "Kept Movie", Year: 2000, Directory: "smb://FREEBOX/Films/Kept/",
                    FileName: "Kept.mkv"),
                new TestKodiMovie(2, "Removed Movie", Year: 2001, Directory: "smb://FREEBOX/Films/Removed/",
                    FileName: "Removed.mkv"),
                new TestKodiMovie(3, "Reidentified Movie", Year: 2002, Directory: "smb://FREEBOX/Films/Reidentified/",
                    FileName: "Reidentified.mkv")
            ],
            uniqueIds:
            [
                new TestKodiUniqueId(1, "movie", "tmdb", "1001"),
                new TestKodiUniqueId(2, "movie", "tmdb", "1002"),
                new TestKodiUniqueId(3, "movie", "tmdb", "1003")
            ]);
    }

    private static string BuildUpdatedFixture()
    {
        return KodiTestDbBuilder.CreateVideoDb(
            movies:
            [
                new TestKodiMovie(1, "Kept Movie", Year: 2000, Directory: "smb://FREEBOX/Films/Kept/",
                    FileName: "Kept.mkv"),
                new TestKodiMovie(4, "New Movie", Year: 2003, Directory: "smb://FREEBOX/Films/New/",
                    FileName: "New.mkv"),
                new TestKodiMovie(3, "Reidentified Movie", Year: 2002, Directory: "smb://FREEBOX/Films/Reidentified/",
                    FileName: "Reidentified.mkv")
            ],
            uniqueIds:
            [
                new TestKodiUniqueId(1, "movie", "tmdb", "1001"),
                new TestKodiUniqueId(4, "movie", "tmdb", "1004"),
                new TestKodiUniqueId(3, "movie", "tmdb", "2003")
            ]);
    }

    private sealed class FakeFileStoreForRecovery : IKodiImportFileStore
    {
        public Task<Result<StoredUpload>> SaveAsync(Stream content, string fileName, long declaredLength, CancellationToken ct)
            => Task.FromResult(Result.Success(new StoredUpload(string.Empty, 0)));

        public void Delete(string? filePath)
        {
            if (filePath is not null && File.Exists(filePath))
                File.Delete(filePath);
        }

        public void PurgeOrphans()
        {
        }
    }
}
