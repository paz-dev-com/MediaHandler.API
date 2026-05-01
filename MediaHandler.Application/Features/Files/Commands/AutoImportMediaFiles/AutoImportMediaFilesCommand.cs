using MediaHandler.Application.Common.Models;
using MediatR;

namespace MediaHandler.Application.Features.Files.Commands.AutoImportMediaFiles;

/// <summary>
///     Command that triggers TMDB auto-matching and import for all <c>MediaFile</c> records
///     whose <c>MediaId</c> is <c>null</c>, without triggering a new NAS scan.
///     Useful for retrying previously failed or skipped files, or after manual
///     <c>MediaFile</c> additions.
/// </summary>
/// <param name="Language">
///     BCP-47 language tag forwarded to TMDB metadata requests (e.g., <c>"en"</c>, <c>"fr"</c>).
///     Defaults to <c>"en"</c> when <c>null</c>.
/// </param>
public record AutoImportMediaFilesCommand(
    string? Language = null) : IRequest<Result<AutoImportResult>>;

/// <summary>
///     Outcome of an auto-import operation that processes pre-existing unlinked
///     <c>MediaFile</c> records.
/// </summary>
/// <param name="TotalUnlinked">
///     Total number of <c>MediaFile</c> records with <c>MediaId == null</c> at the start of the
///     operation.
/// </param>
/// <param name="Matched">Number of files successfully matched and linked to a <c>Media</c> entity.</param>
/// <param name="Skipped">Number of files for which TMDB returned no usable result.</param>
/// <param name="Failed">Number of files that raised an exception during TMDB matching.</param>
/// <param name="Errors">Human-readable error messages collected from failed files.</param>
public record AutoImportResult(
    int TotalUnlinked,
    int Matched,
    int Skipped,
    int Failed,
    IReadOnlyList<string> Errors);