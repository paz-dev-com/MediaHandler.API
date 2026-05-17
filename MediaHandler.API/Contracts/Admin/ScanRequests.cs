using MediaHandler.Domain.Enums;

namespace MediaHandler.API.Contracts.Admin;

/// <summary>
///     Request body for <c>POST /api/v1/admin/scan</c>.
///     An empty <see cref="LibraryRootIds" /> array triggers a scan of all enabled roots.
/// </summary>
public record StartScanRequest(
    Guid[] LibraryRootIds,
    ScanMode Mode,
    string? Language = null);
