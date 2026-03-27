using AutoMapper;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Auth.DTOs;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Auth.Commands.SyncUser;

public record SyncUserCommand(string OktaId, string Email, string? DisplayName, bool IsAdmin) : IRequest<Result<UserDto>>;

public class SyncUserCommandHandler(IApplicationDbContext context, IMapper mapper)
    : IRequestHandler<SyncUserCommand, Result<UserDto>>
{
    public async Task<Result<UserDto>> Handle(SyncUserCommand request, CancellationToken cancellationToken)
    {
        var user = await context.Users
            .FirstOrDefaultAsync(u => u.OktaId == request.OktaId, cancellationToken);

        if (user is null)
        {
            user = new User
            {
                OktaId = request.OktaId,
                Email = request.Email,
                DisplayName = request.DisplayName,
                Role = request.IsAdmin ? UserRole.Admin : UserRole.User
            };
            context.Users.Add(user);
        }
        else
        {
            if (!user.IsActive)
                return Result.Fail<UserDto>("Account is deactivated.");

            user.Email = request.Email;
            user.DisplayName = request.DisplayName ?? user.DisplayName;
            user.UpdatedAt = DateTime.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(mapper.Map<UserDto>(user));
    }
}
