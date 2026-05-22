// GetMediaByIdQueryHandlerTests
// Tests T030, T031

using FluentAssertions;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Features.Media.Queries.GetMediaById;
using MediaHandler.Domain.Enums;
using MediaHandler.Tests.Common;
using NSubstitute;

namespace MediaHandler.Tests.Features.Media;

public class GetMediaByIdQueryHandlerTests
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public GetMediaByIdQueryHandlerTests()
    {
        _context = TestDbContext.Create();
        _currentUser = Substitute.For<ICurrentUserService>();
        _currentUser.OktaId.Returns((string?)null);
    }


    private GetMediaByIdQueryHandler CreateHandler() =>
        new(_context, _currentUser);

    [Fact]
    public async Task GetMediaById_EnrichedTvShow_ReturnsStatusAndNumberOfSeasons()
    {
        var media = new Domain.Entities.Media
        {
            TmdbId = 1234,
            Title = "Breaking Bad",
            Type = MediaType.TvShow,
            Status = "Ended",
            NumberOfSeasons = 5
        };
        _context.Medias.Add(media);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetMediaByIdQuery(media.Id), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("Ended");
        result.Value.NumberOfSeasons.Should().Be(5);
    }

    [Fact]
    public async Task GetMediaById_UnenrichedMedia_ReturnsBothFieldsAsNull()
    {
        var media = new Domain.Entities.Media
        {
            TmdbId = 5678,
            Title = "Unknown Film",
            Type = MediaType.Film,
            Status = null,
            NumberOfSeasons = null
        };
        _context.Medias.Add(media);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetMediaByIdQuery(media.Id), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().BeNull();
        result.Value.NumberOfSeasons.Should().BeNull();
    }

    [Fact]
    public async Task GetMediaById_WithLinkedFilesAndNoOverride_ReturnsComputedRootFolder()
    {
        var media = new Domain.Entities.Media
        {
            TmdbId = 9001,
            Title = "Breaking Bad",
            Type = MediaType.TvShow,
            RootFolder = null
        };
        _context.Medias.Add(media);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        _context.MediaFiles.Add(new Domain.Entities.MediaFile
        {
            MediaId = media.Id,
            FilePath = "/nas/tv/Breaking Bad/Season 1/Episode 1.mkv",
            Fingerprint = "a"
        });
        _context.MediaFiles.Add(new Domain.Entities.MediaFile
        {
            MediaId = media.Id,
            FilePath = "/nas/tv/Breaking Bad/Season 1/Episode 2.mkv",
            Fingerprint = "b"
        });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetMediaByIdQuery(media.Id), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.RootFolder.Should().Be("/nas/tv/Breaking Bad/Season 1");
    }

    [Fact]
    public async Task GetMediaById_WithRootFolderOverride_ReturnsOverrideValue()
    {
        var media = new Domain.Entities.Media
        {
            TmdbId = 9002,
            Title = "Breaking Bad",
            Type = MediaType.TvShow,
            RootFolder = "/mnt/nas/tv/Breaking Bad"
        };
        _context.Medias.Add(media);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        _context.MediaFiles.Add(new Domain.Entities.MediaFile
        {
            MediaId = media.Id,
            FilePath = "/some/other/path/file.mkv",
            Fingerprint = "c"
        });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetMediaByIdQuery(media.Id), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.RootFolder.Should().Be("/mnt/nas/tv/Breaking Bad");
    }

    [Fact]
    public async Task GetMediaById_NoFilesAndNoOverride_ReturnsNullRootFolder()
    {
        var media = new Domain.Entities.Media
        {
            TmdbId = 9003,
            Title = "Unknown",
            Type = MediaType.Film,
            RootFolder = null
        };
        _context.Medias.Add(media);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetMediaByIdQuery(media.Id), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.RootFolder.Should().BeNull();
    }
}
