using System.Security.Claims;
using MediaHandler.Domain.Enums;
using MediaHandler.Domain.Interfaces;
using Microsoft.AspNetCore.Http;

namespace MediaHandler.Infrastructure.Identity;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? UserId
    {
        get
        {
            var userIdClaim = _httpContextAccessor.HttpContext?.User?.FindFirst("sub")?.Value;
            return userIdClaim != null && Guid.TryParse(userIdClaim, out var userId) ? userId : null;
        }
    }

    public string? Email => _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Email)?.Value;

    public string? OktaId => _httpContextAccessor.HttpContext?.User?.FindFirst("sub")?.Value;

    public bool IsAdmin
    {
        get
        {
            var roleClaim = _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Role)?.Value;
            return roleClaim == UserRole.Admin.ToString();
        }
    }
}
