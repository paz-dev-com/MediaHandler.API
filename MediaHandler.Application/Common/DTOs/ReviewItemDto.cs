using MediaHandler.Domain.Enums;

namespace MediaHandler.Application.Common.DTOs;

/// <summary>
/// A single TMDB candidate attached to a <see cref="ReviewItemDto"/>.
/// </summary>
public record TmdbCandidateDto(
    int TmdbId,
    MediaType Kind,
    string Title,
    int? Year,
    decimal? Score,
    string? PosterPath);

/// <summary>
/// Data-transfer object for a <c>ReviewItem</c> row, returned by the review-items API.
/// </summary>
public record ReviewItemDto(
    Guid Id,
    string FilePath,
    ReviewReason Reason,
    ReviewStatus Status,
    string? ParsedTitle,
    int? ParsedYear,
    int? ParsedSeason,
    int? ParsedEpisode,
    IReadOnlyList<TmdbCandidateDto> Candidates,
    int? ResolvedTmdbId,
    MediaType? ResolvedKind,
    DateTime? ResolvedAt,
    DateTime CreatedAt);

