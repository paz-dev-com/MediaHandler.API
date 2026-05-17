using FluentValidation;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Auth.DTOs;
using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Users.Commands.UploadProfilePicture;

public record UploadProfilePictureCommand(
    string OktaId,
    Stream FileStream,
    string FileName,
    string ContentType,
    long FileSize) : IRequest<Result<UserDto>>;

public class UploadProfilePictureCommandValidator : AbstractValidator<UploadProfilePictureCommand>
{
    private static readonly HashSet<string> AllowedContentTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg",
            "image/png",
            "image/webp"
        };

    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".webp"
        };

    public UploadProfilePictureCommandValidator()
    {
        RuleFor(x => x.ContentType)
            .Must(ct => AllowedContentTypes.Contains(ct))
            .WithMessage("Content type must be image/jpeg, image/png, or image/webp.");

        RuleFor(x => x.FileName)
            .Must(fn => AllowedExtensions.Contains(Path.GetExtension(fn).ToLower()))
            .WithMessage("File extension must be .jpg, .jpeg, .png, or .webp.");

        RuleFor(x => x.FileSize)
            .LessThanOrEqualTo(2_097_152)
            .WithMessage("File size must not exceed 2 MB.");
    }
}

public sealed class UploadProfilePictureCommandHandler(
    IApplicationDbContext db,
    IMapper mapper,
    IWebRootProvider webRootProvider)
    : IRequestHandler<UploadProfilePictureCommand, Result<UserDto>>
{
    public async Task<Result<UserDto>> Handle(
        UploadProfilePictureCommand command,
        CancellationToken cancellationToken)
    {
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.OktaId == command.OktaId, cancellationToken);

        if (user is null)
            return Result.Fail<UserDto>("USER_NOT_FOUND: User not found.");

        var newExt = Path.GetExtension(command.FileName).ToLower();
        var uploadsDir = Path.Combine(
            webRootProvider.WebRootPath,
            "uploads",
            "profile-pictures");

        Directory.CreateDirectory(uploadsDir);

        // Delete old file if extension changes
        if (user.ProfilePicturePath is not null)
        {
            var oldExt = Path.GetExtension(user.ProfilePicturePath);
            if (!oldExt.Equals(newExt, StringComparison.OrdinalIgnoreCase))
            {
                var oldFileName = Path.GetFileName(user.ProfilePicturePath);
                var oldFsPath = Path.Combine(uploadsDir, oldFileName);
                if (File.Exists(oldFsPath))
                    File.Delete(oldFsPath);
            }
        }

        var newFileName = $"{user.Id}{newExt}";
        var newFsPath = Path.Combine(uploadsDir, newFileName);

        await using (var fs = new FileStream(newFsPath, FileMode.Create, FileAccess.Write))
        {
            await command.FileStream.CopyToAsync(fs, cancellationToken);
        }

        user.ProfilePicturePath = $"/api/v1/users/profile-picture/{user.Id}{newExt}";
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(mapper.Map<UserDto>(user));
    }
}
