using FluentAssertions;
using MediaHandler.Application.Features.Media.Commands.LinkMediaFile;
using MediaHandler.Application.Features.Media.Commands.UnlinkMediaFile;
using MediaHandler.Domain.Enums;
using MediaHandler.Tests.Common;

namespace MediaHandler.Tests.Features.Media;

public class FileLinkCommandHandlerTests
{
    private readonly TestDbContext _context = TestDbContext.Create();

    private LinkMediaFileCommandHandler CreateLinkHandler() => new(_context);
    private UnlinkMediaFileCommandHandler CreateUnlinkHandler() => new(_context);

    [Fact]
    public async Task LinkFile_WhenFileIsUnlinked_SetsMediaIdAndReturnsSuccess()
    {
        var media = new Domain.Entities.Media { TmdbId = 100, Title = "Test", Type = MediaType.Film };
        var file = new Domain.Entities.MediaFile { FilePath = "/test/file.mkv", Fingerprint = "fp1" };
        _context.Medias.Add(media);
        _context.MediaFiles.Add(file);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateLinkHandler().Handle(
            new LinkMediaFileCommand(media.Id, file.Id),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        var updated = await _context.MediaFiles.FindAsync([file.Id], TestContext.Current.CancellationToken);
        updated!.MediaId.Should().Be(media.Id);
    }

    [Fact]
    public async Task LinkFile_WhenFileAlreadyLinkedToSameMedia_ReturnsSuccessIdempotent()
    {
        var media = new Domain.Entities.Media { TmdbId = 101, Title = "Test", Type = MediaType.Film };
        var file = new Domain.Entities.MediaFile { FilePath = "/test/file.mkv", Fingerprint = "fp2", MediaId = Guid.NewGuid() };
        _context.Medias.Add(media);
        _context.MediaFiles.Add(file);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Set to same media
        file.MediaId = media.Id;
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateLinkHandler().Handle(
            new LinkMediaFileCommand(media.Id, file.Id),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task LinkFile_WhenFileAlreadyLinkedToDifferentMedia_ReturnsFileAlreadyLinkedError()
    {
        var media1 = new Domain.Entities.Media { TmdbId = 102, Title = "Media1", Type = MediaType.Film };
        var media2 = new Domain.Entities.Media { TmdbId = 103, Title = "Media2", Type = MediaType.Film };
        var file = new Domain.Entities.MediaFile { FilePath = "/test/file.mkv", Fingerprint = "fp3" };
        _context.Medias.AddRange(media1, media2);
        _context.MediaFiles.Add(file);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        file.MediaId = media1.Id;
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateLinkHandler().Handle(
            new LinkMediaFileCommand(media2.Id, file.Id),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainMatch("FILE_ALREADY_LINKED*");
    }

    [Fact]
    public async Task LinkFile_WhenMediaIdDoesNotExist_ReturnsNotFound()
    {
        var file = new Domain.Entities.MediaFile { FilePath = "/test/file.mkv", Fingerprint = "fp4" };
        _context.MediaFiles.Add(file);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateLinkHandler().Handle(
            new LinkMediaFileCommand(Guid.NewGuid(), file.Id),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainMatch("NOT_FOUND*");
    }

    [Fact]
    public async Task LinkFile_WhenFileIdDoesNotExist_ReturnsNotFound()
    {
        var media = new Domain.Entities.Media { TmdbId = 104, Title = "Test", Type = MediaType.Film };
        _context.Medias.Add(media);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateLinkHandler().Handle(
            new LinkMediaFileCommand(media.Id, Guid.NewGuid()),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainMatch("NOT_FOUND*");
    }

    [Fact]
    public async Task UnlinkFile_WhenFileIsLinkedToMedia_ClearsMediaIdAndReturnsSuccess()
    {
        var media = new Domain.Entities.Media { TmdbId = 200, Title = "Test", Type = MediaType.Film };
        var file = new Domain.Entities.MediaFile { FilePath = "/test/file.mkv", Fingerprint = "fp5" };
        _context.Medias.Add(media);
        _context.MediaFiles.Add(file);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        file.MediaId = media.Id;
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateUnlinkHandler().Handle(
            new UnlinkMediaFileCommand(media.Id, file.Id),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        var updated = await _context.MediaFiles.FindAsync([file.Id], TestContext.Current.CancellationToken);
        updated!.MediaId.Should().BeNull();
    }

    [Fact]
    public async Task UnlinkFile_WhenFileIsNotLinkedToMedia_ReturnsNotFound()
    {
        var media = new Domain.Entities.Media { TmdbId = 201, Title = "Test", Type = MediaType.Film };
        var file = new Domain.Entities.MediaFile { FilePath = "/test/file.mkv", Fingerprint = "fp6" };
        _context.Medias.Add(media);
        _context.MediaFiles.Add(file);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // file.MediaId is null — not linked to media
        var result = await CreateUnlinkHandler().Handle(
            new UnlinkMediaFileCommand(media.Id, file.Id),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainMatch("NOT_FOUND*");
    }

    [Fact]
    public async Task UnlinkFile_WhenFileIdDoesNotExist_ReturnsNotFound()
    {
        var media = new Domain.Entities.Media { TmdbId = 202, Title = "Test", Type = MediaType.Film };
        _context.Medias.Add(media);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateUnlinkHandler().Handle(
            new UnlinkMediaFileCommand(media.Id, Guid.NewGuid()),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainMatch("NOT_FOUND*");
    }
}

