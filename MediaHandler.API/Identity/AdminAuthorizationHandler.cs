using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
namespace MediaHandler.API.Identity;

/// <summary>
///     Authorization requirement satisfied when the authenticated user has the Admin role in the database.
///     This is DB-driven rather than JWT-driven so that role changes in the database take effect
///     immediately — without waiting for the access token to expire or for an Auth0 Action to be configured.
/// </summary>
public class AdminRequirement : IAuthorizationRequirement;
public class AdminAuthorizationHandler(
    IApplicationDbContext context,
    IHttpContextAccessor httpContextAccessor)
    : AuthorizationHandler<AdminRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext authContext,
        AdminRequirement requirement)
    {
        var sub = httpContextAccessor.HttpContext?.User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(sub))
            return;
        var isAdmin = await context.Users
            .AsNoTracking()
            .Where(u => u.OktaId == sub && u.IsActive)
            .Select(u => (bool?)(u.Role == UserRole.Admin))
            .FirstOrDefaultAsync();
        if (isAdmin == true)
            authContext.Succeed(requirement);
    }
}
