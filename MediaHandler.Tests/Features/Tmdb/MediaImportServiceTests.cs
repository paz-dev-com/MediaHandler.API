using FluentAssertions;
using MediaHandler.Application.Common.DTOs;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Domain.Enums;
using MediaHandler.Infrastructure.Services;
using MediaHandler.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MediaHandler.Tests.Features.Tmdb;

public class MediaImportServiceTests
{
    private static readonly TmdbMediaDetailsDto MatrixDetails = new(
        603,
        "The Matrix",
        "The Matrix",
        "A hacker discovers reality is a simulation.",
        "movie",
        new DateTime(1999, 3, 31),
        136,
        "/poster.jpg",
        "/backdrop.jpg",
        8.7m,
        24000,
        ["Action", "Science Fiction"],
        "en");

    private readonly IApplicationDbContext _context;
    private readonly MediaImportService _service;
    private readonly ITmdbService _tmdb;

    public MediaImportServiceTests()
    {
        _context = TestDbContext.Create();
        _tmdb = Substitute.For<ITmdbService>();
        _service = new MediaImportService(
            _context,
            _tmdb,
            NullLogger<MediaImportService>.Instance);
    }


    [Fact]
    public async Task ImportOrGetExisting_NewTmdbId_CreatesMedia()
    {
        // Arrange
        _tmdb.GetMediaDetailsAsync(603, "movie", "en", Arg.Any<CancellationToken>())
            .Returns(MatrixDetails);

        // Act
        var result = await _service.ImportOrGetExistingAsync(603, "movie", "en", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();

        var media = _context.Medias.FirstOrDefault(m => m.TmdbId == 603);
        media.Should().NotBeNull();
        media!.Title.Should().Be("The Matrix");
        media.Runtime.Should().Be(136);
        media.Language.Should().Be("en");

        var genres = _context.MediaGenres.Where(g => g.MediaId == media.Id).Select(g => g.Name).ToList();
        genres.Should().BeEquivalentTo("Action", "Science Fiction");
    }

    [Fact]
    public async Task ImportOrGetExisting_ExistingTmdbId_ReturnsExistingId()
    {
        // Arrange — Media already exists in the database
        var existing = new Domain.Entities.Media { TmdbId = 603, Title = "The Matrix", Type = MediaType.Film };
        _context.Medias.Add(existing);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        var result = await _service.ImportOrGetExistingAsync(603, "movie", "en", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(existing.Id, "the existing Media.Id must be returned without creating a duplicate");

        // TMDB must NOT have been called — dedup short-circuits before the API call
        await _tmdb.DidNotReceive().GetMediaDetailsAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        _context.Medias.Count().Should().Be(1, "no duplicate Media row must be inserted");
    }


    [Fact]
    public async Task ImportOrGetExisting_TmdbReturnsNull_ReturnsFail()
    {
        // Arrange
        _tmdb.GetMediaDetailsAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((TmdbMediaDetailsDto?)null);

        // Act
        var result = await _service.ImportOrGetExistingAsync(9999, "movie", "en", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("not found on TMDB"));

        _context.Medias.Should().BeEmpty("no Media should be persisted when TMDB returns nothing");
    }
}