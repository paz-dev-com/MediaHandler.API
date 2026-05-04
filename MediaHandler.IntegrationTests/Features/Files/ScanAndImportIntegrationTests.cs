using FluentAssertions;
using MediaHandler.Application.Common.DTOs;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Features.Files.Commands.ScanAndImportNas;
using MediaHandler.Infrastructure.Nas;
using MediaHandler.Infrastructure.Services;
using MediaHandler.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MediaHandler.IntegrationTests.Features.Files;

/// <summary>
///     End-to-end integration tests for the scan-and-import pipeline against a real
///     SQL Server instance via Testcontainers.
///     TMDB and NAS are mocked; the filename parser, import service and auto-match
///     service run against the real database.
/// </summary>
public class ScanAndImportIntegrationTests : IntegrationTestBase
{
    // Shared test data

    // Two movie files that the mocked NAS will "return"
    private static readonly List<NasFileInfo> TestNasFiles =
    [
        new("/nas/Movies/The Matrix 1999/The.Matrix.1999.1080p.mkv",
            "The.Matrix.1999.1080p.mkv", 1_500_000_000L, "mkv", DateTime.UtcNow, DateTime.UtcNow),
        new("/nas/Movies/Inception 2010/Inception.2010.1080p.mkv",
            "Inception.2010.1080p.mkv", 1_200_000_000L, "mkv", DateTime.UtcNow, DateTime.UtcNow)
    ];

    // TMDB search results returned by the mock
    private static readonly TmdbMediaDto MatrixSearchResult = new(
        603, "The Matrix", "The Matrix", null, "movie",
        new DateTime(1999, 3, 31), null, null, 8.7m);

    private static readonly TmdbMediaDto InceptionSearchResult = new(
        27205, "Inception", "Inception", null, "movie",
        new DateTime(2010, 7, 16), null, null, 8.8m);

    // TMDB details returned by the mock
    private static readonly TmdbMediaDetailsDto MatrixDetails = new(
        603, "The Matrix", "The Matrix", "A hacker discovers the truth.", "movie",
        new DateTime(1999, 3, 31), 136, null, null, 8.7m, 24000,
        ["Action", "Science Fiction"], "en");

    private static readonly TmdbMediaDetailsDto InceptionDetails = new(
        27205, "Inception", "Inception", "A thief enters dreams.", "movie",
        new DateTime(2010, 7, 16), 148, null, null, 8.8m, 34000,
        ["Action", "Science Fiction", "Adventure"], "en");

    // Helpers

    private (INasService nas, ITmdbService tmdb, ScanAndImportNasCommandHandler handler) BuildHandler()
    {
        var nas = Substitute.For<INasService>();
        var tmdb = Substitute.For<ITmdbService>();

        // Configure TMDB mock — search by query
        tmdb.SearchMediaAsync(Arg.Is<string>(q => q.Contains("Matrix")), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(MatrixSearchResult);
        tmdb.SearchMediaAsync(Arg.Is<string>(q => q.Contains("Inception")), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(InceptionSearchResult);

        // Configure TMDB details mock
        tmdb.GetMediaDetailsAsync(603, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(MatrixDetails);
        tmdb.GetMediaDetailsAsync(27205, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(InceptionDetails);

        // Real services wired together with the real DbContext
        var importer = new MediaImportService(DbContext, tmdb, NullLogger<MediaImportService>.Instance);
        var parser = new MediaFileNameParser();
        var autoMatcher = new MediaAutoMatchService(parser, tmdb, importer, DbContext,
            NullLogger<MediaAutoMatchService>.Instance);

        var handler = new ScanAndImportNasCommandHandler(
            DbContext, nas, autoMatcher, NullLogger<ScanAndImportNasCommandHandler>.Instance);

        return (nas, tmdb, handler);
    }

    // Tests

    [Fact]
    public async Task ScanAndImport_NewFiles_CreatesMediaAndLinks()
    {
        // Arrange
        var (nas, _, handler) = BuildHandler();
        nas.ScanDirectoryAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(TestNasFiles);

        // Act
        var result = await handler.Handle(
            new ScanAndImportNasCommand(Language: "en"),
            TestContext.Current.CancellationToken);

        // Assert — command result
        result.IsSuccess.Should().BeTrue();
        result.Value.NewFiles.Should().Be(2);
        result.Value.TotalScanned.Should().Be(2);
        result.Value.Matched.Should().Be(2);
        result.Value.Skipped.Should().Be(0);
        result.Value.Failed.Should().Be(0);

        // Assert — MediaFiles persisted
        var mediaFiles = await DbContext.MediaFiles.ToListAsync(TestContext.Current.CancellationToken);
        mediaFiles.Should().HaveCount(2);
        mediaFiles.Should().AllSatisfy(f => f.MediaId.Should().NotBeNull("every file should be linked"));

        // Assert — Media entities created with genres
        var medias = await DbContext.Medias.Include(m => m.Genres).ToListAsync(TestContext.Current.CancellationToken);
        medias.Should().HaveCount(2);

        var matrix = medias.FirstOrDefault(m => m.TmdbId == 603);
        matrix.Should().NotBeNull();
        matrix!.Title.Should().Be("The Matrix");
        matrix.Genres.Select(g => g.Name).Should().BeEquivalentTo("Action", "Science Fiction");

        var inception = medias.FirstOrDefault(m => m.TmdbId == 27205);
        inception.Should().NotBeNull();
        inception!.Genres.Should().HaveCount(3);

        // Assert — MediaFile ↔ Media links
        mediaFiles.First(f => f.FilePath.Contains("Matrix")).MediaId.Should().Be(matrix.Id);
        mediaFiles.First(f => f.FilePath.Contains("Inception")).MediaId.Should().Be(inception.Id);
    }

    [Fact]
    public async Task ScanAndImport_SecondRun_IsIdempotent()
    {
        // Arrange — run 1
        var (nas, _, handler) = BuildHandler();
        nas.ScanDirectoryAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(TestNasFiles);

        var firstRun = await handler.Handle(
            new ScanAndImportNasCommand(Language: "en"),
            TestContext.Current.CancellationToken);

        firstRun.IsSuccess.Should().BeTrue();
        firstRun.Value.Matched.Should().Be(2);

        // Act — run 2 with the same NAS list
        var secondRun = await handler.Handle(
            new ScanAndImportNasCommand(Language: "en"),
            TestContext.Current.CancellationToken);

        // Assert — second run produces zero new data
        secondRun.IsSuccess.Should().BeTrue();
        secondRun.Value.NewFiles.Should().Be(0, "files are already registered");
        secondRun.Value.Matched.Should().Be(0, "files are already linked (MediaId != null)");
        secondRun.Value.Skipped.Should().Be(0);
        secondRun.Value.Failed.Should().Be(0);

        // Database state unchanged
        var mediaFiles = await DbContext.MediaFiles.ToListAsync(TestContext.Current.CancellationToken);
        mediaFiles.Should().HaveCount(2, "no duplicate MediaFile rows");

        var medias = await DbContext.Medias.ToListAsync(TestContext.Current.CancellationToken);
        medias.Should().HaveCount(2, "no duplicate Media rows");
    }
}