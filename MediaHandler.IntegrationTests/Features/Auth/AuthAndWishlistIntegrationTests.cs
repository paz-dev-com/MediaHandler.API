using FluentAssertions;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Features.Auth.Commands.SyncUser;
using MediaHandler.Application.Features.Wishlist.Commands.AddToWishlist;
using MediaHandler.Application.Features.Wishlist.Commands.RemoveFromWishlist;
using MediaHandler.Application.Features.Wishlist.Queries.GetWishlist;
using MediaHandler.Domain.Entities;
using MediaHandler.IntegrationTests.Common;
using NSubstitute;

namespace MediaHandler.IntegrationTests.Features.Auth;

public class AuthAndWishlistIntegrationTests : IntegrationTestBase
{
    [Fact]
    public async Task SyncUser_NewUser_PersistedToDb()
    {
        var handler = new SyncUserCommandHandler(DbContext, Mapper);

        var result = await handler.Handle(
            new SyncUserCommand("okta|int1", "integration@test.com", "Integration User", false),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        DbContext.Users.Should().ContainSingle(u => u.OktaId == "okta|int1");
    }

    [Fact]
    public async Task SyncUser_ExistingUser_UpdatesEmail()
    {
        DbContext.Users.Add(new User { OktaId = "okta|existing", Email = "old@test.com" });
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        var handler = new SyncUserCommandHandler(DbContext, Mapper);

        var result = await handler.Handle(
            new SyncUserCommand("okta|existing", "new@test.com", null, false),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        DbContext.Users.Single(u => u.OktaId == "okta|existing").Email.Should().Be("new@test.com");
    }

    [Fact]
    public async Task AddAndRemoveWishlistItem_RoundTrip()
    {
        var user = new User { OktaId = "okta|wish1", Email = "wish@test.com" };
        DbContext.Users.Add(user);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.OktaId.Returns("okta|wish1");

        var addHandler = new AddToWishlistCommandHandler(DbContext, currentUser);
        var addResult = await addHandler.Handle(
            new AddToWishlistCommand(550, "Fight Club", null, null, null),
            CancellationToken.None);
        addResult.IsSuccess.Should().BeTrue();

        var getHandler = new GetWishlistQueryHandler(DbContext, currentUser, Mapper);
        var getResult = await getHandler.Handle(new GetWishlistQuery(), CancellationToken.None);
        getResult.Value.TotalCount.Should().Be(1);

        var removeHandler = new RemoveFromWishlistCommandHandler(DbContext, currentUser);
        await removeHandler.Handle(new RemoveFromWishlistCommand(addResult.Value), CancellationToken.None);

        var afterRemove = await getHandler.Handle(new GetWishlistQuery(), CancellationToken.None);
        afterRemove.Value.TotalCount.Should().Be(0);
    }
}
