using MediaHandler.Domain.Enums;

namespace MediaHandler.API.Contracts.Admin;

/// <summary>
///     Request body for <c>POST /api/v1/admin/library-roots</c>.
/// </summary>
public record AddLibraryRootRequest(
    string Path,
    LibraryRootKind Kind,
    string? Label);