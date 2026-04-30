#nullable enable
// AddLibraryRootCommandHandlerTests — Library root registration validation

using FluentAssertions;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Features.LibraryRoots.Commands.AddLibraryRoot;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Enums;
using MediaHandler.Tests.Common;
using NSubstitute;

namespace MediaHandler.Tests.Features.LibraryRoots;

public class AddLibraryRootCommandHandlerTests
{
    private readonly IApplicationDbContext _context = TestDbContext.Create();
    private readonly INasService _nasService = Substitute.For<INasService>();

    private AddLibraryRootCommandHandler CreateHandler() =>
        new(_context, _nasService);

    public AddLibraryRootCommandHandlerTests()
    {
        // Default: /nas is configured
        _nasService.GetConfiguredPathsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["/nas", "/nas2"]));
    }

    [Fact]
    public async Task Handle_ValidNewRoot_CreatesAndReturnsDto()
    {
        var command = new AddLibraryRootCommand("/nas/Movies", LibraryRootKind.Movies, "My Movies");
        var handler = CreateHandler();

        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Path.Should().Be("/nas/Movies");
        result.Value.Kind.Should().Be(LibraryRootKind.Movies);
        result.Value.Label.Should().Be("My Movies");
        result.Value.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_DuplicatePath_ReturnsConflict()
    {
        var existing = new LibraryRoot
        {
            Path = "/nas/Movies",
            Kind = LibraryRootKind.Movies,
            IsEnabled = true
        };
        _context.LibraryRoots.Add(existing);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new AddLibraryRootCommand("/nas/Movies", LibraryRootKind.Movies, null);
        var handler = CreateHandler();

        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("LIBRARY_ROOT_DUPLICATE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Handle_PathOutsideConfiguredBases_ReturnsValidationFailure()
    {
        // /unconfigured is not under /nas or /nas2
        var command = new AddLibraryRootCommand("/unconfigured/Movies", LibraryRootKind.Movies, null);
        var handler = CreateHandler();

        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_EmptyPath_ReturnsValidationFailure()
    {
        var command = new AddLibraryRootCommand("", LibraryRootKind.Movies, null);
        var handler = CreateHandler();

        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_PathExceedingMaxLength_ReturnsValidationFailure()
    {
        var longPath = "/nas/" + new string('a', 1020);
        var command = new AddLibraryRootCommand(longPath, LibraryRootKind.Movies, null);
        var handler = CreateHandler();

        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_TvShowsRoot_CreatesWithCorrectKind()
    {
        var command = new AddLibraryRootCommand("/nas/TV", LibraryRootKind.TvShows, "TV Shows");
        var handler = CreateHandler();

        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Kind.Should().Be(LibraryRootKind.TvShows);
    }
}

