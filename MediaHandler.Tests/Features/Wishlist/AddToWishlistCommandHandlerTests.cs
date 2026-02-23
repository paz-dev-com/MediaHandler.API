using FluentAssertions;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Features.Wishlist.Commands.AddToWishlist;
using MediaHandler.Domain.Entities;
using MediaHandler.Tests.Common;
using NSubstitute;

namespace MediaHandler.Tests.Features.Wishlist;

public class AddToWishlistCommandHandlerTests
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly AddToWishlistCommandHandler _handler;

    private static readonly string TestOktaId = "okta|test123";
    private static readonly Guid TestUserId = Guid.NewGuid();

    public AddToWishlistCommandHandlerTests()
    {
        _context = TestDbContext.Create();

        _currentUser = Substitute.For<ICurrentUserService>();
        _currentUser.OktaId.Returns(TestOktaId);

        _context.Users.Add(new User { Id = TestUserId, OktaId = TestOktaId, Email = "test@example.com" });
        _context.SaveChangesAsync().GetAwaiter().GetResult();

        _handler = new AddToWishlistCommandHandler(_context, _currentUser);
    }

    [Fact]
    public async Task Handle_NewItem_AddsToWishlistAndReturnsId()
    {
        var command = new AddToWishlistCommand(12345, "Test Movie", "/poster.jpg", null, null);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();
        _context.WishlistItems.Count().Should().Be(1);
    }

    [Fact]
    public async Task Handle_DuplicateItem_ReturnsFailResult()
    {
        _context.WishlistItems.Add(new WishlistItem { UserId = TestUserId, TmdbId = 12345, Title = "Test Movie" });
        await _context.SaveChangesAsync();

        var command = new AddToWishlistCommand(12345, "Test Movie", null, null, null);
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain("This title is already in your wishlist.");
    }
}
