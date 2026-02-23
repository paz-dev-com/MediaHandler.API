namespace MediaHandler.Application.Common.Interfaces;

public interface ICurrentUserService
{
    Guid? UserId { get; }
    string? Email { get; }
    string? OktaId { get; }
    bool IsAdmin { get; }
}
