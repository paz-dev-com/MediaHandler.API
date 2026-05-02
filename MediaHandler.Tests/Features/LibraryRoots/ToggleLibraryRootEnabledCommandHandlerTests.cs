// ToggleLibraryRootEnabledCommandHandlerTests — Toggle library root enabled status

using FluentAssertions;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Features.LibraryRoots.Commands.ToggleLibraryRootEnabled;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Enums;
using MediaHandler.Tests.Common;

namespace MediaHandler.Tests.Features.LibraryRoots;

public class ToggleLibraryRootEnabledCommandHandlerTests
{
    private readonly IApplicationDbContext _context = TestDbContext.Create();

    private ToggleLibraryRootEnabledCommandHandler CreateHandler()
    {
        return new ToggleLibraryRootEnabledCommandHandler(_context);
    }

    [Fact]
    public async Task Handle_ToggleTrueToFalse_ReturnsUpdatedDto()
    {
        // Arrange
        var root = new LibraryRoot
        {
            Path = "/nas/Movies",
            Kind = LibraryRootKind.Movies,
            IsEnabled = true
        };
        _context.LibraryRoots.Add(root);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new ToggleLibraryRootEnabledCommand(root.Id, false);
        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.IsEnabled.Should().BeFalse();
        result.Value.Id.Should().Be(root.Id);
    }

    [Fact]
    public async Task Handle_ToggleFalseToTrue_ReturnsUpdatedDto()
    {
        // Arrange
        var root = new LibraryRoot
        {
            Path = "/nas/TV",
            Kind = LibraryRootKind.TvShows,
            IsEnabled = false
        };
        _context.LibraryRoots.Add(root);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new ToggleLibraryRootEnabledCommand(root.Id, true);
        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.IsEnabled.Should().BeTrue();
        result.Value.Id.Should().Be(root.Id);
    }

    [Fact]
    public async Task Handle_RootNotFound_ReturnsNotFoundFailure()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        var command = new ToggleLibraryRootEnabledCommand(nonExistentId, false);
        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("NOT_FOUND", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Handle_ScanInProgress_ReturnsScanInProgressFailure()
    {
        // Arrange
        var root = new LibraryRoot
        {
            Path = "/nas/Music",
            Kind = LibraryRootKind.Movies,
            IsEnabled = true
        };
        _context.LibraryRoots.Add(root);

        var activeScan = new ScanRun
        {
            Mode = ScanMode.Full,
            Status = ScanStatus.Running,
            LibraryRootIdsJson = "[]", // empty = ALL roots
            StartedAt = DateTime.UtcNow
        };
        _context.ScanRuns.Add(activeScan);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new ToggleLibraryRootEnabledCommand(root.Id, false);
        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("SCAN_IN_PROGRESS", StringComparison.OrdinalIgnoreCase));
    }
}




