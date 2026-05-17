// DeleteProfilePictureCommandHandlerTests
// Tests T022, T023, T024

using AutoMapper;
using FluentAssertions;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Mappings;
using MediaHandler.Application.Features.Users.Commands.DeleteProfilePicture;
using MediaHandler.Domain.Entities;
using MediaHandler.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace MediaHandler.Tests.Features.Users;

public class DeleteProfilePictureCommandHandlerTests : IDisposable
{
    private readonly IApplicationDbContext _context;
    private readonly IMapper _mapper;
    private readonly IWebRootProvider _webRootProvider;
    private readonly string _tempDir;

    public DeleteProfilePictureCommandHandlerTests()
    {
        _context = TestDbContext.Create();
        _mapper = new ServiceCollection()
            .AddLogging()
            .AddAutoMapper(cfg => cfg.AddProfile<UserMappingProfile>())
            .BuildServiceProvider()
            .GetRequiredService<IMapper>();

        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);

        _webRootProvider = Substitute.For<IWebRootProvider>();
        _webRootProvider.WebRootPath.Returns(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private DeleteProfilePictureCommandHandler CreateHandler() =>
        new(_context, _mapper, _webRootProvider);

    private async Task<User> AddUserWithPicture(string? picturePath = null)
    {
        var user = new User
        {
            OktaId = "okta|test",
            Email = "test@example.com",
            ProfilePicturePath = picturePath
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return user;
    }

    [Fact]
    public async Task DeleteProfilePicture_WithExistingPicture_ClearsPathAndReturnsUpdatedDto()
    {
        var user = await AddUserWithPicture();
        var uploadsDir = Path.Combine(_tempDir, "uploads", "profile-pictures");
        Directory.CreateDirectory(uploadsDir);
        var fileName = $"{user.Id}.jpg";
        var filePath = Path.Combine(uploadsDir, fileName);
        await File.WriteAllBytesAsync(filePath, [1, 2, 3], TestContext.Current.CancellationToken);

        user.ProfilePicturePath = $"/api/v1/users/profile-picture/{fileName}";
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new DeleteProfilePictureCommand("okta|test");
        var handler = CreateHandler();
        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.ProfilePicturePath.Should().BeNull();
        File.Exists(filePath).Should().BeFalse();

        var dbUser = _context.Users.Single(u => u.OktaId == "okta|test");
        dbUser.ProfilePicturePath.Should().BeNull();
    }

    [Fact]
    public async Task DeleteProfilePicture_NoPicture_ReturnsNotFoundFailure()
    {
        await AddUserWithPicture();

        var command = new DeleteProfilePictureCommand("okta|test");
        var handler = CreateHandler();
        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.Contains("USER_HAS_NO_PROFILE_PICTURE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DeleteProfilePicture_FileAlreadyGone_StillClearsDatabasePath()
    {
        var user = await AddUserWithPicture();
        var fileName = $"{user.Id}.jpg";
        user.ProfilePicturePath = $"/api/v1/users/profile-picture/{fileName}";
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // File does NOT exist on disk — this should NOT throw
        var command = new DeleteProfilePictureCommand("okta|test");
        var handler = CreateHandler();
        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.ProfilePicturePath.Should().BeNull();

        var dbUser = _context.Users.Single(u => u.OktaId == "okta|test");
        dbUser.ProfilePicturePath.Should().BeNull();
    }
}



