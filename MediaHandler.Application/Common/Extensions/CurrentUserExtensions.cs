using MediaHandler.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Common.Extensions;

internal static class CurrentUserExtensions
{
    /// <summary>
    /// Resolves the internal database UserId from the current user's OktaId.
    /// Throws <see cref="UnauthorizedAccessException"/> if the user is not authenticated or not found in the database.
    /// </summary>
    internal static async Task<Guid> ResolveUserIdAsync(
        this ICurrentUserService currentUser,
        IApplicationDbContext context,
        CancellationToken cancellationToken)
    {
        var oktaId = currentUser.OktaId ?? throw new UnauthorizedAccessException();

        return await context.Users
            .Where(u => u.OktaId == oktaId)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new UnauthorizedAccessException();
    }
}
