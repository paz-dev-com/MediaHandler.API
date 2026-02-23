using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Domain.Enums;
using MediatR;

namespace MediaHandler.Application.Features.Admin.Commands.SetUserRole;

public record SetUserRoleCommand(Guid UserId, UserRole Role) : IRequest<Result>;

public class SetUserRoleCommandHandler(IApplicationDbContext context)
    : IRequestHandler<SetUserRoleCommand, Result>
{
    public async Task<Result> Handle(SetUserRoleCommand request, CancellationToken cancellationToken)
    {
        var user = await context.Users.FindAsync([request.UserId], cancellationToken);

        if (user is null)
            return Result.Fail("User not found.");

        user.Role = request.Role;
        user.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
