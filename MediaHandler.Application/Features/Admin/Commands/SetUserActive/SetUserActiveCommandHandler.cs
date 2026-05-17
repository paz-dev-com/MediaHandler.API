using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediatR;

namespace MediaHandler.Application.Features.Admin.Commands.SetUserActive;

public record SetUserActiveCommand(Guid UserId, bool IsActive) : IRequest<Result>;

public class SetUserActiveCommandHandler(IApplicationDbContext context)
    : IRequestHandler<SetUserActiveCommand, Result>
{
    public async Task<Result> Handle(SetUserActiveCommand request, CancellationToken cancellationToken)
    {
        var user = await context.Users.FindAsync([request.UserId], cancellationToken);

        if (user is null)
            return Result.Fail("User not found.");

        user.IsActive = request.IsActive;
        user.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}