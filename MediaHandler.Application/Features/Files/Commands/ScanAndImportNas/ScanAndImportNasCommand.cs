using MediaHandler.Application.Common.Models;
using MediatR;

namespace MediaHandler.Application.Features.Files.Commands.ScanAndImportNas;

/// <summary>
///     Command that triggers a NAS scan followed by automatic TMDB matching and import
///     for all newly detected (and previously unlinked) <c>MediaFile</c> records.
/// </summary>
/// <param name="BasePath">
///     Optional NAS base path to restrict the scan. When <c>null</c>, all configured
///     base paths are scanned.
/// </param>
/// <param name="Language">
///     BCP-47 language tag forwarded to TMDB metadata requests (e.g., <c>"en"</c>, <c>"fr"</c>).
///     Defaults to <c>"en"</c> when <c>null</c>.
/// </param>
public record ScanAndImportNasCommand(
    string? BasePath = null,
    string? Language = null) : IRequest<Result<ScanAndImportNasResult>>;

/// <summary>
///     Aggregated outcome of a scan-and-import operation, combining NAS scan statistics
///     with TMDB auto-match counters.
/// </summary>
/// <param name="NewFiles">Number of new <c>MediaFile</c> records discovered and added during this scan.</param>
/// <param name="ExistingFiles">Number of file paths already present in the database.</param>
/// <param name="TotalScanned">Total number of files found on the NAS (excluding directories).</param>
/// <param name="FoldersFound">Number of directory entries returned by the NAS scan.</param>
/// <param name="Matched">Number of unlinked <c>MediaFile</c> records successfully linked to a <c>Media</c> entity.</param>
/// <param name="Skipped">Number of files for which TMDB returned no usable result.</param>
/// <param name="Failed">Number of files that raised an exception during TMDB matching.</param>
/// <param name="Errors">Human-readable error messages collected from failed files.</param>
public record ScanAndImportNasResult(
    int NewFiles,
    int ExistingFiles,
    int TotalScanned,
    int FoldersFound,
    int Matched,
    int Skipped,
    int Failed,
    IReadOnlyList<string> Errors);