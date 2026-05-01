using FluentAssertions;
using MediaHandler.Application.Common.DTOs;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Features.Files.Commands.AutoImportMediaFiles;
using MediaHandler.Domain.Entities;
using MediaHandler.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MediaHandler.Tests.Features.Files;

public class AutoImportMediaFilesCommandHandlerTests
{
    private readonly IMediaAutoMatchService _autoMatchService;
    private readonly IApplicationDbContext _context;
    private readonly AutoImportMediaFilesCommandHandler _handler;

    public AutoImportMediaFilesCommandHandlerTests()
    {
        _context = TestDbContext.Create();
        _autoMatchService = Substitute.For<IMediaAutoMatchService>();
        _handler = new AutoImportMediaFilesCommandHandler(
            _context,
            _autoMatchService,
            NullLogger<AutoImportMediaFilesCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_UnlinkedFilesExist_MatchesOnlyUnlinked()
    {
        // Arrange — 2 unlinked files + 1 already linked file
        var linkedMediaId = Guid.NewGuid();

        _context.MediaFiles.Add(new MediaFile { FilePath = "/Movies/Already.Linked.mkv", MediaId = linkedMediaId });
        _context.MediaFiles.Add(new MediaFile { FilePath = "/Movies/The.Matrix.1999.mkv" });
        _context.MediaFiles.Add(new MediaFile { FilePath = "/Movies/Inception.2010.mkv" });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        _autoMatchService
            .MatchAndLinkUnlinkedFilesAsync(
                Arg.Any<IReadOnlyList<MediaFile>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new AutoMatchResult(2, 0, 0, []));

        // Act
        var result = await _handler.Handle(
            new AutoImportMediaFilesCommand("en"),
            TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.TotalUnlinked.Should().Be(2, "only the 2 unlinked files must be processed");
        result.Value.Matched.Should().Be(2);
        result.Value.Skipped.Should().Be(0);
        result.Value.Failed.Should().Be(0);
        result.Value.Errors.Should().BeEmpty();

        // Verify the service was called with exactly the 2 unlinked files
        await _autoMatchService.Received(1).MatchAndLinkUnlinkedFilesAsync(
            Arg.Is<IReadOnlyList<MediaFile>>(list =>
                list.Count == 2 &&
                list.All(f => f.MediaId == null)),
            "en",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoUnlinkedFiles_ReturnsZeroCounts()
    {
        // Arrange — all files already linked
        var mediaId = Guid.NewGuid();
        _context.MediaFiles.Add(new MediaFile { FilePath = "/Movies/Already.Linked.mkv", MediaId = mediaId });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        var result = await _handler.Handle(
            new AutoImportMediaFilesCommand(),
            TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.TotalUnlinked.Should().Be(0);
        result.Value.Matched.Should().Be(0);
        result.Value.Skipped.Should().Be(0);
        result.Value.Failed.Should().Be(0);

        // Verify the matching service is NOT called when there's nothing to process
        await _autoMatchService.DidNotReceive().MatchAndLinkUnlinkedFilesAsync(
            Arg.Any<IReadOnlyList<MediaFile>>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EmptyDatabase_ReturnsZeroCounts()
    {
        // Act — empty DB, no files at all
        var result = await _handler.Handle(
            new AutoImportMediaFilesCommand(),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.TotalUnlinked.Should().Be(0);
        result.Value.Matched.Should().Be(0);

        await _autoMatchService.DidNotReceive().MatchAndLinkUnlinkedFilesAsync(
            Arg.Any<IReadOnlyList<MediaFile>>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PartialMatchResult_AggregatesCountsCorrectly()
    {
        // Arrange — 3 unlinked files: 1 matched, 1 skipped, 1 failed
        _context.MediaFiles.Add(new MediaFile { FilePath = "/Movies/Matrix.mkv" });
        _context.MediaFiles.Add(new MediaFile { FilePath = "/Movies/Unknown.mkv" });
        _context.MediaFiles.Add(new MediaFile { FilePath = "/Movies/Corrupt.mkv" });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        _autoMatchService
            .MatchAndLinkUnlinkedFilesAsync(
                Arg.Any<IReadOnlyList<MediaFile>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new AutoMatchResult(1, 1, 1, ["/Movies/Corrupt.mkv — parse error"]));

        // Act
        var result = await _handler.Handle(
            new AutoImportMediaFilesCommand("fr"),
            CancellationToken.None);

        // Assert
        result.Value.TotalUnlinked.Should().Be(3);
        result.Value.Matched.Should().Be(1);
        result.Value.Skipped.Should().Be(1);
        result.Value.Failed.Should().Be(1);
        result.Value.Errors.Should().HaveCount(1);
    }
}