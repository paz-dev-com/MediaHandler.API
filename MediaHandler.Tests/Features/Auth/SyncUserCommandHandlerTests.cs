using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Mappings;
using MediaHandler.Application.Features.Auth.Commands.SyncUser;
using MediaHandler.Domain.Entities;
using MediaHandler.Tests.Common;
using NSubstitute;

namespace MediaHandler.Tests.Features.Auth;

public class SyncUserCommandHandlerTests
{
    private readonly IApplicationDbContext _context;
    private readonly SyncUserCommandHandler _handler;

    public SyncUserCommandHandlerTests()
    {
        _context = TestDbContext.Create();
        var mapper = new ServiceCollection()
            .AddLogging()
            .AddAutoMapper(cfg => cfg.AddProfile<UserMappingProfile>())
            .BuildServiceProvider()
            .GetRequiredService<IMapper>();
        _handler = new SyncUserCommandHandler(_context, mapper);
    }

    [Fact]
    public async Task Handle_NewUser_CreatesAndReturnsUser()
    {
        var command = new SyncUserCommand("okta|new123", "new@example.com", "New User", IsAdmin: false);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Email.Should().Be("new@example.com");
        result.Value.DisplayName.Should().Be("New User");
        _context.Users.Count().Should().Be(1);
    }

    [Fact]
    public async Task Handle_NewAdminUser_CreatesUserWithAdminRole()
    {
        var command = new SyncUserCommand("okta|admin123", "admin@example.com", "Admin User", IsAdmin: true);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Role.Should().Be("Admin");
    }

    [Fact]
    public async Task Handle_ExistingActiveUser_UpdatesEmailAndDisplayName()
    {
        _context.Users.Add(new User { OktaId = "okta|existing", Email = "old@example.com", DisplayName = "Old Name" });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new SyncUserCommand("okta|existing", "new@example.com", "New Name", IsAdmin: false);
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Email.Should().Be("new@example.com");
        result.Value.DisplayName.Should().Be("New Name");
    }

    [Fact]
    public async Task Handle_DeactivatedUser_ReturnsFailResult()
    {
        _context.Users.Add(new User { OktaId = "okta|inactive", Email = "inactive@example.com", IsActive = false });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new SyncUserCommand("okta|inactive", "inactive@example.com", null, IsAdmin: false);
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain("Account is deactivated.");
    }
}
