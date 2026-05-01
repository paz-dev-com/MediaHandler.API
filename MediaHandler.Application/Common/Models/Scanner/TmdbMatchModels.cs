using MediaHandler.Domain.Enums;

namespace MediaHandler.Application.Common.Models.Scanner;

/// <summary>
///     A single TMDB candidate returned as part of a <see cref="TmdbMatchResult" />.
/// </summary>
public record TmdbCandidate(
    int TmdbId,
    MediaType Kind,
    string Title,
    int? Year,
    /// <summary>Popularity-derived ranking score (higher = better match).</summary>
    decimal Score,
    string? PosterPath);

/// <summary>
///     Query input for <c>ITmdbMatcher.ResolveAsync</c>.
///     Precedence chain enforced by the implementation:
///     <c>NfoTmdbId → ExplicitTokenId → Title+Year → Title</c>.
/// </summary>
public record MatchQuery(
    string Title,
    int? Year,
    MediaType? KindHint,
    /// <summary>TMDB id extracted from an <c>.nfo</c> sidecar (highest precedence).</summary>
    int? NfoTmdbId = null,
    /// <summary>TMDB id extracted from an explicit token in the filename (second precedence).</summary>
    int? ExplicitTokenId = null,
    string Language = "en-US");

/// <summary>
///     Result of <c>ITmdbMatcher.ResolveAsync</c>.
///     When <see cref="NeedsReview" /> is <c>true</c>, the scanner creates a
///     <c>ReviewItem</c> instead of mapping the file to a <c>Media</c> row.
/// </summary>
public record TmdbMatchResult(
    bool IsMatched,
    int? TmdbId,
    MediaType? Kind,
    bool NeedsReview,
    ReviewReason? ReviewReason,
    IReadOnlyList<TmdbCandidate> Candidates);