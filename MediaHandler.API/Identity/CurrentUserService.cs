using System.Security.Claims;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Domain.Enums;

namespace MediaHandler.API.Identity;

public class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{

    public Guid? UserId
    {
        get
        {
            var userIdClaim = httpContextAccessor.HttpContext?.User?.FindFirst("sub")?.Value;
            return userIdClaim != null && Guid.TryParse(userIdClaim, out var userId) ? userId : null;
        }
    }

    public string? Email => httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Email)?.Value;

    public string? OktaId => httpContextAccessor.HttpContext?.User?.FindFirst("sub")?.Value;

    public bool IsAdmin
    {
        get
        {
            var roleClaim = httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Role)?.Value;
            return roleClaim == UserRole.Admin.ToString();
        }
    }
}
