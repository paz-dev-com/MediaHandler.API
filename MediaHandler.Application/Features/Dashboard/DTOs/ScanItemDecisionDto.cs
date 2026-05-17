using MediaHandler.Application.Common.DTOs;
using MediaHandler.Domain.Enums;

namespace MediaHandler.Application.Features.Dashboard.DTOs;

/// <summary>
///     Data-transfer object for a <see cref="MediaHandler.Domain.Entities.ScanItemDecision" /> row,
///     returned by the scan decisions browser API.
///     Field names are aligned with the Angular frontend contract (camelCase via default serialiser).
/// </summary>
public record ScanItemDecisionDto(
    Guid Id,
    Guid ScanRunId,
    string FilePath,
    /// <summary>Outcome kind — serialised as <c>decisionType</c>.</summary>
    ScanDecisionKind DecisionType,
    string? Reason,
    int? AssignedTmdbId,
    /// <summary>Media type of the TMDB assignment — serialised as <c>assignedKind</c>.</summary>
    MediaType? AssignedKind,
    string? AssignedTitle,
    int? AssignedYear,
    string? AssignedPosterPath,
    /// <summary>Parsed TMDB candidates as a typed list (never null — empty list when absent).</summary>
    IReadOnlyList<TmdbCandidateDto> Candidates,
    string? ParsedTitle,
    int? ParsedYear,
    int? ParsedSeason,
    int? ParsedEpisode,
    /// <summary>Media type inferred by the scanner — serialised as <c>mediaType</c>.</summary>
    MediaType? MediaType,
    Guid? LibraryRootId,
    Guid? MediaFileId,
    /// <summary>Row creation timestamp — serialised as <c>decidedAt</c>.</summary>
    DateTime DecidedAt);
