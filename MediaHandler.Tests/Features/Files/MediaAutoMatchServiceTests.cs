using FluentAssertions;
using MediaHandler.Application.Common.DTOs;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Domain.Entities;
using MediaHandler.Infrastructure.Services;
using MediaHandler.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MediaHandler.Tests.Features.Files;

public class MediaAutoMatchServiceTests
{
    private static readonly TmdbMediaDto MatrixSearchResult = new(
        603,
        "The Matrix",
        "The Matrix",
        null,
        "movie",
        new DateTime(1999, 3, 31),
        null,
        null,
        8.7m);

    private readonly IMediaImportService _importer;
    private readonly IMediaFileNameParser _parser;
    private readonly MediaAutoMatchService _service;
    private readonly ITmdbService _tmdb;

    public MediaAutoMatchServiceTests()
    {
        IApplicationDbContext context = TestDbContext.Create();
        _parser = Substitute.For<IMediaFileNameParser>();
        _tmdb = Substitute.For<ITmdbService>();
        _importer = Substitute.For<IMediaImportService>();

        _service = new MediaAutoMatchService(
            _parser,
            _tmdb,
            _importer,
            context,
            NullLogger<MediaAutoMatchService>.Instance);
    }

    [Fact]
    public async Task MatchAndLink_ParseSuccessTmdbFound_LinksMediaFile()
    {
        // Arrange
        var mediaId = Guid.NewGuid();
        var file = new MediaFile { FilePath = "/Movies/The.Matrix.1999.mkv" };

        _parser.Parse(file.FilePath)
            .Returns(new ParsedMediaInfo("The Matrix", 1999, "movie"));

        _tmdb.SearchMediaAsync("The Matrix 1999", "en", Arg.Any<CancellationToken>())
            .Returns(new List<TmdbMediaDto> { MatrixSearchResult });

        _importer.ImportOrGetExistingAsync(603, "movie", "en", Arg.Any<CancellationToken>())
            .Returns(Result.Success(mediaId));

        // Act
        var result = await _service.MatchAndLinkUnlinkedFilesAsync([file], "en", CancellationToken.None);

        // Assert
        result.Matched.Should().Be(1);
        result.Skipped.Should().Be(0);
        result.Failed.Should().Be(0);
        result.Errors.Should().BeEmpty();

        file.MediaId.Should().Be(mediaId, "the MediaFile should be linked to the imported Media");
    }

    [Fact]
    public async Task MatchAndLink_ParseFails_IncrementsFailedCount()
    {
        // Arrange — parser returns null (cannot extract title from filename)
        var file = new MediaFile { FilePath = "/Movies/encrypted_file_xyz_abc.mkv" };

        _parser.Parse(file.FilePath).Returns((ParsedMediaInfo?)null);

        // Act
        var result = await _service.MatchAndLinkUnlinkedFilesAsync([file], "en", CancellationToken.None);

        // Assert
        result.Failed.Should().Be(1);
        result.Matched.Should().Be(0);
        result.Skipped.Should().Be(0);
        result.Errors.Should().HaveCount(1);

        file.MediaId.Should().BeNull("the file must remain unlinked when parsing fails");

        // TMDB must not be called when we cannot produce a search query
        await _tmdb.DidNotReceive().SearchMediaAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MatchAndLink_TmdbNoResult_IncrementsSkippedCount()
    {
        // Arrange — parser succeeds but TMDB search returns nothing
        var file = new MediaFile { FilePath = "/Movies/Unknown.Movie.2099.mkv" };

        _parser.Parse(file.FilePath)
            .Returns(new ParsedMediaInfo("Unknown Movie", 2099, null));

        _tmdb.SearchMediaAsync("Unknown Movie 2099", "en", Arg.Any<CancellationToken>())
            .Returns(new List<TmdbMediaDto>());
        // Fallback search without year also returns nothing
        _tmdb.SearchMediaAsync("Unknown Movie", "en", Arg.Any<CancellationToken>())
            .Returns(new List<TmdbMediaDto>());

        // Act
        var result = await _service.MatchAndLinkUnlinkedFilesAsync([file], "en", CancellationToken.None);

        // Assert
        result.Skipped.Should().Be(1);
        result.Matched.Should().Be(0);
        result.Failed.Should().Be(0);

        file.MediaId.Should().BeNull();

        // Import service must not be called when TMDB finds nothing
        await _importer.DidNotReceive().ImportOrGetExistingAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MatchAndLink_ImportFails_IncrementsFailedCount()
    {
        // Arrange — parse and search succeed but import returns failure
        var file = new MediaFile { FilePath = "/Movies/The.Matrix.1999.mkv" };

        _parser.Parse(file.FilePath)
            .Returns(new ParsedMediaInfo("The Matrix", 1999, "movie"));

        _tmdb.SearchMediaAsync("The Matrix 1999", "en", Arg.Any<CancellationToken>())
            .Returns(new List<TmdbMediaDto> { MatrixSearchResult });

        _importer.ImportOrGetExistingAsync(603, "movie", "en", Arg.Any<CancellationToken>())
            .Returns(Result.Fail<Guid>("TMDB returned no details."));

        // Act
        var result = await _service.MatchAndLinkUnlinkedFilesAsync([file], "en", CancellationToken.None);

        // Assert
        result.Failed.Should().Be(1);
        result.Matched.Should().Be(0);
        result.Errors.Should().HaveCount(1);

        file.MediaId.Should().BeNull();
    }
}