using FluentAssertions;
using MediaHandler.Application.Common.DTOs;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Features.Files.Commands.ScanAndImportNas;
using MediaHandler.Domain.Entities;
using MediaHandler.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MediaHandler.Tests.Features.Files;

public class ScanAndImportNasCommandHandlerTests
{
    private readonly IMediaAutoMatchService _autoMatchService;
    private readonly IApplicationDbContext _context;
    private readonly ScanAndImportNasCommandHandler _handler;
    private readonly INasService _nasService;

    public ScanAndImportNasCommandHandlerTests()
    {
        _context = TestDbContext.Create();
        _nasService = Substitute.For<INasService>();
        _autoMatchService = Substitute.For<IMediaAutoMatchService>();
        _handler = new ScanAndImportNasCommandHandler(
            _context,
            _nasService,
            _autoMatchService,
            NullLogger<ScanAndImportNasCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_NewFilesDetected_ScansAndMatchesSuccessfully()
    {
        // Arrange
        var nasFiles = new List<NasFileInfo>
        {
            new("/Movies/The.Matrix.1999.1080p.mkv", "The.Matrix.1999.1080p.mkv",
                1_500_000_000L, "mkv", DateTime.UtcNow, DateTime.UtcNow),
            new("/Movies/Inception.2010.mkv", "Inception.2010.mkv",
                1_200_000_000L, "mkv", DateTime.UtcNow, DateTime.UtcNow)
        };

        _nasService
            .ScanDirectoryAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(nasFiles);

        _autoMatchService
            .MatchAndLinkUnlinkedFilesAsync(
                Arg.Any<IReadOnlyList<MediaFile>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new AutoMatchResult(2, 0, 0, []));

        // Act
        var result = await _handler.Handle(
            new ScanAndImportNasCommand(null, "en"),
            TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.NewFiles.Should().Be(2);
        result.Value.ExistingFiles.Should().Be(0);
        result.Value.TotalScanned.Should().Be(2);
        result.Value.Matched.Should().Be(2);
        result.Value.Skipped.Should().Be(0);
        result.Value.Failed.Should().Be(0);
        result.Value.Errors.Should().BeEmpty();

        // Verify matching service was called with the 2 unlinked files
        await _autoMatchService.Received(1).MatchAndLinkUnlinkedFilesAsync(
            Arg.Is<IReadOnlyList<MediaFile>>(list => list.Count == 2),
            "en",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NewFilesDetected_FolderEntriesAreExcludedFromFileCount()
    {
        // Arrange — 1 file + 1 directory returned by NAS
        var nasEntries = new List<NasFileInfo>
        {
            new("/Movies/The.Matrix.1999.mkv", "The.Matrix.1999.mkv",
                1_000_000_000L, "mkv", DateTime.UtcNow, DateTime.UtcNow),
            new("/Movies/", "Movies", 0, null, DateTime.UtcNow, DateTime.UtcNow, true)
        };

        _nasService
            .ScanDirectoryAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(nasEntries);

        _autoMatchService
            .MatchAndLinkUnlinkedFilesAsync(Arg.Any<IReadOnlyList<MediaFile>>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new AutoMatchResult(1, 0, 0, []));

        // Act
        var result = await _handler.Handle(new ScanAndImportNasCommand(), CancellationToken.None);

        // Assert
        result.Value.TotalScanned.Should().Be(1, "directories must not be counted as scanned files");
        result.Value.FoldersFound.Should().Be(1);
        result.Value.NewFiles.Should().Be(1);
    }

    [Fact]
    public async Task Handle_NoFilesOnNas_ReturnsZeroCounts()
    {
        // Arrange — NAS returns nothing
        _nasService
            .ScanDirectoryAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Enumerable.Empty<NasFileInfo>());

        _autoMatchService
            .MatchAndLinkUnlinkedFilesAsync(Arg.Any<IReadOnlyList<MediaFile>>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new AutoMatchResult(0, 0, 0, []));

        // Act
        var result = await _handler.Handle(new ScanAndImportNasCommand(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.NewFiles.Should().Be(0);
        result.Value.TotalScanned.Should().Be(0);
        result.Value.Matched.Should().Be(0);
        result.Value.Skipped.Should().Be(0);
        result.Value.Failed.Should().Be(0);
    }

    [Fact]
    public async Task Handle_FileAlreadyInDatabase_IsNotAddedAgain()
    {
        // Arrange — file already exists in DB
        _context.MediaFiles.Add(new MediaFile
        {
            FilePath = "/Movies/The.Matrix.1999.mkv"
        });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var nasFiles = new List<NasFileInfo>
        {
            new("/Movies/The.Matrix.1999.mkv", "The.Matrix.1999.mkv",
                1_000_000_000L, "mkv", DateTime.UtcNow, DateTime.UtcNow)
        };

        _nasService
            .ScanDirectoryAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(nasFiles);

        _autoMatchService
            .MatchAndLinkUnlinkedFilesAsync(Arg.Any<IReadOnlyList<MediaFile>>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new AutoMatchResult(0, 1, 0, []));

        // Act
        var result = await _handler.Handle(new ScanAndImportNasCommand(), CancellationToken.None);

        // Assert
        result.Value.NewFiles.Should().Be(0, "the file was already registered");
        result.Value.ExistingFiles.Should().Be(1);
        _context.MediaFiles.Count().Should().Be(1, "no duplicate MediaFile should be created");
    }
}