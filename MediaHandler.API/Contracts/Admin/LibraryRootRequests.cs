using MediaHandler.Domain.Enums;

namespace MediaHandler.API.Contracts.Admin;

/// <summary>
///     Request body for <c>POST /api/v1/admin/library-roots</c>.
/// </summary>
public record AddLibraryRootRequest(
    string Path,
    LibraryRootKind Kind,
    string? Label);

/// <summary>
///     Request body for <c>PUT /api/v1/admin/library-roots/{id}</c>.
/// </summary>
public record UpdateLibraryRootRequest(LibraryRootKind Kind, string? Label);

/// <summary>
///     Request body for <c>PUT /api/v1/admin/library-roots/{id}/enabled</c>.
/// </summary>
public record ToggleLibraryRootEnabledRequest(bool IsEnabled);
