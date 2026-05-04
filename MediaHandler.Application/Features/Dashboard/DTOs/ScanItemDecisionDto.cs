using MediaHandler.Domain.Enums;

namespace MediaHandler.Application.Features.Dashboard.DTOs;

/// <summary>
///     Data-transfer object for a <see cref="MediaHandler.Domain.Entities.ScanItemDecision" /> row,
///     returned by the scan decisions browser API.
///     <para>
///         <c>assignedTitle</c>, <c>assignedYear</c>, and <c>assignedPosterPath</c> are resolved
///         by joining <c>MediaFile → Media</c> in the query handler (not stored on the decision row).
///         <c>libraryRootPath</c> is resolved from <c>LibraryRoot.Path</c>.
///     </para>
/// </summary>
public record ScanItemDecisionDto(
    Guid Id,
    Guid ScanRunId,
    string FilePath,
    ScanDecisionKind Kind,
    string? Reason,
    int? AssignedTmdbId,
    MediaType? AssignedTmdbKind,
    string? AssignedTitle,
    int? AssignedYear,
    string? AssignedPosterPath,
    string? CandidatesJson,
    string? ParsedTitle,
    int? ParsedYear,
    int? ParsedSeason,
    int? ParsedEpisode,
    MediaType? ParsedMediaType,
    Guid? LibraryRootId,
    string? LibraryRootPath,
    Guid? MediaFileId);

