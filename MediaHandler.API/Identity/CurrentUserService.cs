using System.Security.Claims;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Domain.Enums;

namespace MediaHandler.API.Identity;

public class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    // Auth0/Okta access tokens use the raw "email" claim, not the XML-schema form
    // (ClaimTypes.Email = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress").
    public string? Email =>
        httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Email)
        ?? httpContextAccessor.HttpContext?.User.FindFirstValue("email");

    public string? OktaId => httpContextAccessor.HttpContext?.User.FindFirstValue("sub");

    public bool IsAdmin =>
        httpContextAccessor.HttpContext?.User.IsInRole(nameof(UserRole.Admin)) ?? false;
}