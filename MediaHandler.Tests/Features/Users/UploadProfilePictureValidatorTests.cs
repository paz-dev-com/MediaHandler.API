// UploadProfilePictureValidatorTests
// Tests T025, T026, T027

using FluentAssertions;
using MediaHandler.Application.Features.Users.Commands.UploadProfilePicture;

namespace MediaHandler.Tests.Features.Users;

public class UploadProfilePictureValidatorTests
{
    private readonly UploadProfilePictureCommandValidator _validator = new();

    private static UploadProfilePictureCommand ValidCommand(
        string contentType = "image/jpeg",
        string fileName = "photo.jpg",
        long fileSize = 1_000_000)
        => new("okta|test", Stream.Null, fileName, contentType, fileSize);

    [Fact]
    public void Validate_ValidJpeg_PassesValidation()
    {
        var result = _validator.Validate(ValidCommand());
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ValidJpegWithJpegExtension_PassesValidation()
    {
        var result = _validator.Validate(ValidCommand("image/jpeg", "photo.jpeg"));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ValidPng_PassesValidation()
    {
        var result = _validator.Validate(ValidCommand("image/png", "photo.png"));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ValidWebp_PassesValidation()
    {
        var result = _validator.Validate(ValidCommand("image/webp", "photo.webp"));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_UnsupportedContentType_FailsValidation()
    {
        var result = _validator.Validate(ValidCommand("image/gif", "photo.gif"));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UploadProfilePictureCommand.ContentType));
    }

    [Fact]
    public void Validate_UnsupportedExtension_FailsValidation()
    {
        var result = _validator.Validate(ValidCommand("image/bmp", "photo.bmp"));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_FileSizeExceeds2MB_FailsValidation()
    {
        var result = _validator.Validate(ValidCommand(fileSize: 2_097_153));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UploadProfilePictureCommand.FileSize));
    }

    [Fact]
    public void Validate_FileSizeExactly2MB_PassesValidation()
    {
        var result = _validator.Validate(ValidCommand(fileSize: 2_097_152));
        result.IsValid.Should().BeTrue();
    }
}

