// ProfilePictureEndpointTests — T032
// Full lifecycle integration test: upload → GET /auth/me → delete → verify file gone

using FluentAssertions;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Features.Auth.Queries.GetCurrentUser;
using MediaHandler.Application.Features.Users.Commands.DeleteProfilePicture;
using MediaHandler.Application.Features.Users.Commands.UploadProfilePicture;
using MediaHandler.Domain.Entities;
using MediaHandler.IntegrationTests.Common;
using NSubstitute;

namespace MediaHandler.IntegrationTests.Users;

public class ProfilePictureEndpointTests : IntegrationTestBase, IDisposable
{
    private readonly string _tempDir;

    public ProfilePictureEndpointTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task Upload_ThenGetMe_ThenDelete_FullProfilePictureLifecycle()
    {
        // Arrange: create user in DB
        var user = new User { OktaId = "okta|pictest", Email = "pic@test.com" };
        DbContext.Users.Add(user);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.OktaId.Returns("okta|pictest");

        var webRootProvider = Substitute.For<IWebRootProvider>();
        webRootProvider.WebRootPath.Returns(_tempDir);

        // --- Step 1: Upload profile picture ---
        var jpegBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        using var uploadStream = new MemoryStream(jpegBytes);

        var uploadHandler = new UploadProfilePictureCommandHandler(DbContext, Mapper, webRootProvider);
        var uploadResult = await uploadHandler.Handle(
            new UploadProfilePictureCommand(
                "okta|pictest", uploadStream, "avatar.jpg", "image/jpeg", jpegBytes.Length),
            TestContext.Current.CancellationToken);

        uploadResult.IsSuccess.Should().BeTrue();
        uploadResult.Value.ProfilePicturePath.Should().NotBeNull();
        uploadResult.Value.ProfilePicturePath.Should().StartWith("/api/v1/users/profile-picture/");
        uploadResult.Value.ProfilePicturePath.Should().EndWith(".jpg");

        var profilePicturePath = uploadResult.Value.ProfilePicturePath!;

        // --- Step 2: GET /auth/me — confirm path in UserDto ---
        var getMeHandler = new GetCurrentUserQueryHandler(DbContext, currentUser, Mapper);
        var getMeResult = await getMeHandler.Handle(
            new GetCurrentUserQuery(),
            TestContext.Current.CancellationToken);

        getMeResult.IsSuccess.Should().BeTrue();
        getMeResult.Value.ProfilePicturePath.Should().Be(profilePicturePath);

        // --- Step 3: Delete profile picture ---
        var deleteHandler = new DeleteProfilePictureCommandHandler(DbContext, Mapper, webRootProvider);
        var deleteResult = await deleteHandler.Handle(
            new DeleteProfilePictureCommand("okta|pictest"),
            TestContext.Current.CancellationToken);

        deleteResult.IsSuccess.Should().BeTrue();
        deleteResult.Value.ProfilePicturePath.Should().BeNull();

        // --- Step 4: Verify file is gone from disk ---
        var fileName = Path.GetFileName(profilePicturePath);
        var filePath = Path.Combine(_tempDir, "uploads", "profile-pictures", fileName);
        File.Exists(filePath).Should().BeFalse();

        // GET /auth/me after delete should return null path
        var getMeAfterDelete = await getMeHandler.Handle(
            new GetCurrentUserQuery(),
            TestContext.Current.CancellationToken);

        getMeAfterDelete.IsSuccess.Should().BeTrue();
        getMeAfterDelete.Value.ProfilePicturePath.Should().BeNull();
    }
}


