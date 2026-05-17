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
    string Language = "en-US",
    /// <summary>
    ///     Alternative title to retry with after <see cref="Title" /> exhausts all languages.
    ///     Typically the folder-hierarchy-derived show name when it differs from the filename-derived title.
    ///     Callers MUST ensure <c>FallbackTitle != Title</c> (set to <c>null</c> when they are equal)
    ///     to avoid issuing duplicate TMDB calls with no benefit.
    /// </summary>
    string? FallbackTitle = null,
    /// <summary>
    ///     Ordered list of BCP-47 language tags to try for TMDB searches (e.g., <c>["fr-FR", "en-US"]</c>).
    ///     When non-null, takes <em>full precedence</em> over <see cref="Language" /> — the matcher
    ///     iterates this list and ignores <see cref="Language" /> entirely.
    ///     When null, the matcher falls back to <see cref="Language" /> for backward compatibility.
    /// </summary>
    IReadOnlyList<string>? SearchLanguages = null);

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