using System.Security.Claims;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Domain.Enums;

namespace MediaHandler.API.Identity;

public class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    public string? Email => httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Email);

    public string? OktaId => httpContextAccessor.HttpContext?.User?.FindFirstValue("sub");

    public bool IsAdmin =>
        httpContextAccessor.HttpContext?.User?.IsInRole(UserRole.Admin.ToString()) ?? false;
}
