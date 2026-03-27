using FluentAssertions;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Features.Media.Commands.DeleteMedia;
using MediaHandler.Domain.Entities;
using MediaHandler.Tests.Common;

namespace MediaHandler.Tests.Features.Media;

public class DeleteMediaCommandHandlerTests
{
    private readonly IApplicationDbContext _context;
    private readonly DeleteMediaCommandHandler _handler;

    public DeleteMediaCommandHandlerTests()
    {
        _context = TestDbContext.Create();
        _handler = new DeleteMediaCommandHandler(_context);
    }

    [Fact]
    public async Task Handle_ExistingMedia_DeletesAndReturnsSuccess()
    {
        var media = new Domain.Entities.Media { TmdbId = 1, Title = "Test Movie", Type = Domain.Enums.MediaType.Film };
        _context.Medias.Add(media);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _handler.Handle(new DeleteMediaCommand(media.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _context.Medias.Count().Should().Be(0);
    }

    [Fact]
    public async Task Handle_NonExistentMedia_ReturnsFailResult()
    {
        var result = await _handler.Handle(new DeleteMediaCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain("Media not found.");
    }
}
