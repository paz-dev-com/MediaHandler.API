using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Auth.DTOs;
using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Users.Commands.DeleteProfilePicture;

public record DeleteProfilePictureCommand(string OktaId) : IRequest<Result<UserDto>>;

public sealed class DeleteProfilePictureCommandHandler(
    IApplicationDbContext db,
    IMapper mapper,
    IWebRootProvider webRootProvider)
    : IRequestHandler<DeleteProfilePictureCommand, Result<UserDto>>
{
    public async Task<Result<UserDto>> Handle(
        DeleteProfilePictureCommand command,
        CancellationToken cancellationToken)
    {
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.OktaId == command.OktaId, cancellationToken);

        if (user is null)
            return Result.Fail<UserDto>("USER_NOT_FOUND: User not found.");

        if (user.ProfilePicturePath is null)
            return Result.Fail<UserDto>("USER_HAS_NO_PROFILE_PICTURE");

        var uploadsDir = Path.Combine(
            webRootProvider.WebRootPath,
            "uploads",
            "profile-pictures");

        var fsPath = Path.Combine(uploadsDir, Path.GetFileName(user.ProfilePicturePath));

        if (File.Exists(fsPath))
            File.Delete(fsPath);

        user.ProfilePicturePath = null;
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(mapper.Map<UserDto>(user));
    }
}
