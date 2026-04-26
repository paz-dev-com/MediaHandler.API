using MediaHandler.Application.Common.Models.Scanner;

namespace MediaHandler.Application.Common.Interfaces;

/// <summary>
/// Resolves a parsed media name against TMDB using the R-001 precedence chain:
/// <c>NfoTmdbId → ExplicitTokenId → Title+Year → Title</c>.
/// </summary>
/// <remarks>
/// Implementations MUST:
/// <list type="bullet">
///   <item>Maintain an in-process LRU cache keyed on <c>(query, year, kind)</c>.</item>
///   <item>Return <see cref="TmdbMatchResult"/> with <c>NeedsReview = true</c> on ambiguous / missing results.</item>
///   <item>Surface transient HTTP errors as <c>NeedsReview = true</c> without aborting the scan run (FR-017).</item>
/// </list>
/// </remarks>
public interface ITmdbMatcher
{
    /// <summary>
    /// Resolves <paramref name="query"/> against TMDB.
    /// </summary>
    /// <param name="query">The match query carrying title, optional year, and id hints.</param>
    /// <param name="ct">Cancellation token propagated from the scan run.</param>
    Task<TmdbMatchResult> ResolveAsync(MatchQuery query, CancellationToken ct = default);
}

