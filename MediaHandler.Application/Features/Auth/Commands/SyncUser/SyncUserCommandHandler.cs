using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Auth.DTOs;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Auth.Commands.SyncUser;

public record SyncUserCommand(string OktaId, string Email, string? DisplayName) : IRequest<Result<UserDto>>;

public class SyncUserCommandHandler : IRequestHandler<SyncUserCommand, Result<UserDto>>
{
    private readonly IApplicationDbContext _context;

    public SyncUserCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<UserDto>> Handle(SyncUserCommand request, CancellationToken cancellationToken)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.OktaId == request.OktaId, cancellationToken);

        if (user is null)
        {
            user = new User
            {
                OktaId = request.OktaId,
                Email = request.Email,
                DisplayName = request.DisplayName
            };
            _context.Users.Add(user);
        }
        else
        {
            user.Email = request.Email;
            user.DisplayName = request.DisplayName ?? user.DisplayName;
            user.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new UserDto(user.Id, user.Email, user.DisplayName,
            user.PreferredLanguage, user.Role.ToString(), user.IsActive));
    }
}
