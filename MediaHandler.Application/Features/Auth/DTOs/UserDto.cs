namespace MediaHandler.Application.Features.Auth.DTOs;

public record UserDto(
    Guid Id,
    string Email,
    string? DisplayName,
    string PreferredLanguage,
    string Role,
    bool IsActive);