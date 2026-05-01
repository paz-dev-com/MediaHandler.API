using MediaHandler.Domain.Enums;

namespace MediaHandler.API.Contracts.Admin;

public record SetRoleRequest(UserRole Role);

public record SetActiveRequest(bool IsActive);