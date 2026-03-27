using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Mappings;
using MediaHandler.Application.Features.Admin.Commands.SetUserActive;
using MediaHandler.Application.Features.Admin.Commands.SetUserRole;
using MediaHandler.Application.Features.Admin.Queries.GetUsers;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Enums;
using MediaHandler.Tests.Common;

namespace MediaHandler.Tests.Features.Admin;

public class AdminHandlerTests
{
    private readonly IApplicationDbContext _context;
    private readonly IMapper _mapper;

    public AdminHandlerTests()
    {
        _context = TestDbContext.Create();
        _mapper = new ServiceCollection()
            .AddLogging()
            .AddAutoMapper(cfg => cfg.AddProfile<UserMappingProfile>())
            .BuildServiceProvider()
            .GetRequiredService<IMapper>();
    }

    [Fact]
    public async Task GetUsers_ReturnsPaginatedUsers()
    {
        _context.Users.AddRange(
            new User { OktaId = "okta-alice", Email = "alice@example.com" },
            new User { OktaId = "okta-bob", Email = "bob@example.com" },
            new User { OktaId = "okta-carol", Email = "carol@example.com" });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new GetUsersQueryHandler(_context, _mapper);
        var result = await handler.Handle(new GetUsersQuery(Page: 1, PageSize: 2), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(3);
        result.Value.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task SetUserRole_ExistingUser_UpdatesRole()
    {
        var user = new User { OktaId = "okta-user", Email = "user@example.com", Role = UserRole.User };
        _context.Users.Add(user);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new SetUserRoleCommandHandler(_context);
        var result = await handler.Handle(new SetUserRoleCommand(user.Id, UserRole.Admin), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _context.Users.First().Role.Should().Be(UserRole.Admin);
    }

    [Fact]
    public async Task SetUserRole_NonExistentUser_ReturnsFailResult()
    {
        var handler = new SetUserRoleCommandHandler(_context);
        var result = await handler.Handle(new SetUserRoleCommand(Guid.NewGuid(), UserRole.Admin), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain("User not found.");
    }

    [Fact]
    public async Task SetUserActive_ExistingUser_UpdatesActiveStatus()
    {
        var user = new User { OktaId = "okta-user", Email = "user@example.com", IsActive = true };
        _context.Users.Add(user);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new SetUserActiveCommandHandler(_context);
        var result = await handler.Handle(new SetUserActiveCommand(user.Id, false), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _context.Users.First().IsActive.Should().BeFalse();
    }
}
