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

/// <summary>
///     Request body for <c>POST /api/v1/admin/enrichment/start</c>.
///     An absent or null <see cref="Language" /> falls back to <c>en-US</c> inside the coordinator.
/// </summary>
public record StartEnrichmentRequest(string? Language = null);

