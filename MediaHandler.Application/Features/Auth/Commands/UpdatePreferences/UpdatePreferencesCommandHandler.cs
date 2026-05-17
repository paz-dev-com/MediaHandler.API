using AutoMapper;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Auth.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Auth.Commands.UpdatePreferences;

public record UpdatePreferencesCommand(string PreferredLanguage) : IRequest<Result<UserDto>>;

public class UpdatePreferencesCommandHandler(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    IMapper mapper)
    : IRequestHandler<UpdatePreferencesCommand, Result<UserDto>>
{
    public async Task<Result<UserDto>> Handle(UpdatePreferencesCommand request, CancellationToken cancellationToken)
    {
        var user = await context.Users
            .FirstOrDefaultAsync(u => u.OktaId == currentUser.OktaId, cancellationToken);

        if (user is null)
            return Result.Fail<UserDto>("User not found.");

        user.PreferredLanguage = request.PreferredLanguage;
        user.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(mapper.Map<UserDto>(user));
    }
}