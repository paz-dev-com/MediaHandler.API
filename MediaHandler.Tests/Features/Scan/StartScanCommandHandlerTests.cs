#nullable enable
// StartScanCommandHandlerTests — Scan initiation and concurrency guard

using FluentAssertions;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Features.Scan.Commands.StartScan;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Enums;
using MediaHandler.Tests.Common;
using NSubstitute;

namespace MediaHandler.Tests.Features.Scan;

public class StartScanCommandHandlerTests
{
    private readonly IApplicationDbContext _context = TestDbContext.Create();
    private readonly IScanRunCoordinator _coordinator = Substitute.For<IScanRunCoordinator>();

    private StartScanCommandHandler CreateHandler() =>
        new(_context, _coordinator);

    private async Task<LibraryRoot> AddEnabledRoot()
    {
        var root = new LibraryRoot
        {
            Path = "/nas/Movies",
            Kind = LibraryRootKind.Movies,
            IsEnabled = true
        };
        _context.LibraryRoots.Add(root);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return root;
    }

    [Fact]
    public async Task Handle_ValidRequest_ReturnsScanRunHandle()
    {
        var root = await AddEnabledRoot();

        _coordinator.StartAsync(Arg.Any<Application.Common.Models.Scanner.ScanStartParameters>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var p = ci.Arg<Application.Common.Models.Scanner.ScanStartParameters>();
                return Task.FromResult(new Application.Common.Models.Scanner.ScanRunHandle(p.ScanRunId));
            });

        var command = new StartScanCommand([root.Id], ScanMode.Full);
        var handler = CreateHandler();
        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.ScanRunId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handle_EmptyRootIds_ScansAllEnabledRoots()
    {
        await AddEnabledRoot();

        _coordinator.StartAsync(Arg.Any<Application.Common.Models.Scanner.ScanStartParameters>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var p = ci.Arg<Application.Common.Models.Scanner.ScanStartParameters>();
                return Task.FromResult(new Application.Common.Models.Scanner.ScanRunHandle(p.ScanRunId));
            });

        var command = new StartScanCommand([], ScanMode.Full); // empty = all roots
        var handler = CreateHandler();
        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ActiveScanRunning_ReturnsConflict()
    {
        var root = await AddEnabledRoot();

        // Add an existing running scan run
        _context.ScanRuns.Add(new ScanRun
        {
            Mode = ScanMode.Full,
            Status = ScanStatus.Running,
            LibraryRootIdsJson = "[]"
        });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new StartScanCommand([root.Id], ScanMode.Full);
        var handler = CreateHandler();
        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("SCAN_IN_PROGRESS", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Handle_NonExistentRootId_ReturnsValidationFailure()
    {
        var command = new StartScanCommand([Guid.NewGuid()], ScanMode.Full);
        var handler = CreateHandler();
        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_DisabledRoot_ReturnsValidationFailure()
    {
        var root = new LibraryRoot
        {
            Path = "/nas/Disabled",
            Kind = LibraryRootKind.Movies,
            IsEnabled = false
        };
        _context.LibraryRoots.Add(root);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new StartScanCommand([root.Id], ScanMode.Full);
        var handler = CreateHandler();
        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
    }
}

