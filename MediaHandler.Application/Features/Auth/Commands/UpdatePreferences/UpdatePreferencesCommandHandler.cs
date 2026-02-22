using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Auth.DTOs;
using MediaHandler.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Auth.Commands.UpdatePreferences;

public record UpdatePreferencesCommand(string PreferredLanguage) : IRequest<Result<UserDto>>;

public class UpdatePreferencesCommandHandler : IRequestHandler<UpdatePreferencesCommand, Result<UserDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public UpdatePreferencesCommandHandler(IApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<UserDto>> Handle(UpdatePreferencesCommand request, CancellationToken cancellationToken)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.OktaId == _currentUser.OktaId, cancellationToken);

        if (user is null)
            return Result.Fail<UserDto>("User not found.");

        user.PreferredLanguage = request.PreferredLanguage;
        user.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new UserDto(user.Id, user.Email, user.DisplayName,
            user.PreferredLanguage, user.Role.ToString(), user.IsActive));
    }
}
