// UploadProfilePictureCommandHandlerTests
// Tests T019, T020, T021

using AutoMapper;
using FluentAssertions;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Mappings;
using MediaHandler.Application.Features.Users.Commands.UploadProfilePicture;
using MediaHandler.Domain.Entities;
using MediaHandler.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
namespace MediaHandler.Tests.Features.Users;

public class UploadProfilePictureCommandHandlerTests : IDisposable
{
    private readonly IApplicationDbContext _context;
    private readonly IMapper _mapper;
    private readonly IWebRootProvider _webRootProvider;
    private readonly string _tempDir;

    public UploadProfilePictureCommandHandlerTests()
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

    private UploadProfilePictureCommandHandler CreateHandler() =>
        new(_context, _mapper, _webRootProvider);

    private async Task<User> AddUser(string oktaId = "okta|test", string? existingPicturePath = null)
    {
        var user = new User
        {
            OktaId = oktaId,
            Email = "test@example.com",
            ProfilePicturePath = existingPicturePath
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return user;
    }

    [Fact]
    public async Task UploadProfilePicture_WithValidJpeg_ReturnsUpdatedUserDtoWithProfilePicturePath()
    {
        var user = await AddUser();
        var fileContent = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }; // JPEG magic bytes
        using var stream = new MemoryStream(fileContent);

        var command = new UploadProfilePictureCommand(
            "okta|test", stream, "photo.jpg", "image/jpeg", fileContent.Length);

        var handler = CreateHandler();
        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.ProfilePicturePath.Should().NotBeNull();
        result.Value.ProfilePicturePath.Should().Be($"/api/v1/users/profile-picture/{user.Id}.jpg");
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken); // flush
        var dbUser = _context.Users.Single(u => u.OktaId == "okta|test");
        dbUser.ProfilePicturePath.Should().Be($"/api/v1/users/profile-picture/{user.Id}.jpg");
    }

    [Fact]
    public async Task UploadProfilePicture_UserNotFound_ReturnsFailureResult()
    {
        using var stream = new MemoryStream(new byte[10]);
        var command = new UploadProfilePictureCommand(
            "okta|nonexistent", stream, "photo.jpg", "image/jpeg", 10);

        var handler = CreateHandler();
        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("USER_NOT_FOUND", StringComparison.OrdinalIgnoreCase));

        // No file should have been written
        var uploadsDir = Path.Combine(_tempDir, "uploads", "profile-pictures");
        if (Directory.Exists(uploadsDir))
            Directory.GetFiles(uploadsDir).Should().BeEmpty();
    }

    [Fact]
    public async Task UploadProfilePicture_ExtensionChanges_DeletesOldFileBeforeSavingNew()
    {
        var user = await AddUser();
        var oldPath = $"/api/v1/users/profile-picture/{user.Id}.jpg";
        user.ProfilePicturePath = oldPath;
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Create the old file on disk
        var uploadsDir = Path.Combine(_tempDir, "uploads", "profile-pictures");
        Directory.CreateDirectory(uploadsDir);
        var oldFilePath = Path.Combine(uploadsDir, $"{user.Id}.jpg");
        await File.WriteAllBytesAsync(oldFilePath, [1, 2, 3], TestContext.Current.CancellationToken);

        using var stream = new MemoryStream([0x89, 0x50, 0x4E, 0x47]); // PNG magic bytes
        var command = new UploadProfilePictureCommand(
            "okta|test", stream, "photo.png", "image/png", 4);

        var handler = CreateHandler();
        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.ProfilePicturePath.Should().EndWith(".png");

        // Old .jpg file should be deleted
        File.Exists(oldFilePath).Should().BeFalse();

        // New .png file should exist
        var newFilePath = Path.Combine(uploadsDir, $"{user.Id}.png");
        File.Exists(newFilePath).Should().BeTrue();
    }
}




