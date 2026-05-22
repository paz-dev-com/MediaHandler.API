using FluentAssertions;
using MediaHandler.Application.Features.Media.Commands.UpdateMediaRootFolder;
using MediaHandler.Domain.Enums;
using MediaHandler.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Tests.Features.Media;

public class UpdateMediaRootFolderCommandHandlerTests
{
    private readonly TestDbContext _context = TestDbContext.Create();

    private UpdateMediaRootFolderCommandHandler CreateHandler() => new(_context);

    [Fact]
    public async Task UpdateRootFolder_WithValidPath_SetsOverrideAndReturnsSuccess()
    {
        var media = new Domain.Entities.Media { TmdbId = 400, Title = "Breaking Bad", Type = MediaType.TvShow };
        _context.Medias.Add(media);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateHandler().Handle(
            new UpdateMediaRootFolderCommand(media.Id, "/mnt/nas/tv/Breaking Bad"),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        var reloaded = await _context.Medias.AsNoTracking().FirstAsync(m => m.Id == media.Id, cancellationToken: TestContext.Current.CancellationToken);
        reloaded.RootFolder.Should().Be("/mnt/nas/tv/Breaking Bad");
    }

    [Fact]
    public async Task UpdateRootFolder_WithNullValue_ClearsOverride()
    {
        var media = new Domain.Entities.Media
        {
            TmdbId = 401,
            Title = "Breaking Bad",
            Type = MediaType.TvShow,
            RootFolder = "/mnt/nas/tv/Breaking Bad"
        };
        _context.Medias.Add(media);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateHandler().Handle(
            new UpdateMediaRootFolderCommand(media.Id, null),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        var reloaded = await _context.Medias.AsNoTracking().FirstAsync(m => m.Id == media.Id, cancellationToken: TestContext.Current.CancellationToken);
        reloaded.RootFolder.Should().BeNull();
    }

    [Fact]
    public async Task UpdateRootFolder_WithEmptyString_TreatsAsNullAndClearsOverride()
    {
        var media = new Domain.Entities.Media
        {
            TmdbId = 402,
            Title = "Breaking Bad",
            Type = MediaType.TvShow,
            RootFolder = "/mnt/nas/tv/Breaking Bad"
        };
        _context.Medias.Add(media);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateHandler().Handle(
            new UpdateMediaRootFolderCommand(media.Id, ""),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        var reloaded = await _context.Medias.AsNoTracking().FirstAsync(m => m.Id == media.Id, cancellationToken: TestContext.Current.CancellationToken);
        reloaded.RootFolder.Should().BeNull();
    }

    [Fact]
    public async Task UpdateRootFolder_WhenMediaNotFound_ReturnsNotFound()
    {
        var result = await CreateHandler().Handle(
            new UpdateMediaRootFolderCommand(Guid.NewGuid(), "/some/path"),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Errors.First().Should().StartWith("NOT_FOUND");
    }
}

