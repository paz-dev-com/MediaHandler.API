using MediaHandler.Domain.Enums;

namespace MediaHandler.Application.Common.DTOs;

/// <summary>
/// Data-transfer object for a configured <c>LibraryRoot</c>.
/// Used by both the list and the create response.
/// </summary>
public record LibraryRootDto(
    Guid Id,
    string Path,
    LibraryRootKind Kind,
    string? Label,
    bool IsEnabled,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

