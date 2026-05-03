// TmdbMatcher — resolves parsed file metadata to a TMDB entry using the precedence chain:
//   NfoTmdbId → ExplicitTokenId → Multi-language title search → FallbackTitle retry → NeedsReview
// Maintains a per-scan ConcurrentDictionary cache keyed on (title, language, year?, kind?) to
// prevent duplicate TMDB API calls within the same scan run.
// Transient HTTP failures are caught and surfaced as NeedsReview without aborting the scan.

using System.Collections.Concurrent;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models.Scanner;
using MediaHandler.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaHandler.Infrastructure.Nas.Scanner;

/// <summary>
///     Production implementation of <see cref="ITmdbMatcher" />.
///     Wraps <see cref="ITmdbService" /> and applies:
///     <list type="bullet">
///         <item>Precedence chain: NfoTmdbId → ExplicitTokenId → Multi-language title search → FallbackTitle → NeedsReview</item>
///         <item>
///             Ambiguity policy: ≥ 2 candidates within 5 % popularity gap →
///             <see cref="ReviewReason.MultipleCandidates" />
///         </item>
///         <item>Year tolerance: mismatch &gt; ±1 → <see cref="ReviewReason.YearMismatch" /></item>
///         <item>
///             Per-scan deduplication cache keyed on <c>(title, language, year?, kind?)</c> using
///             <see cref="ConcurrentDictionary{TKey,TValue}" />. Only successful matches are cached;
///             NeedsReview results are not cached so transient failures can be retried.
///         </item>
///         <item>Transient error tolerance: <see cref="HttpRequestException" /> caught, result surfaced as NeedsReview</item>
///     </list>
/// </summary>
public sealed class TmdbMatcher : ITmdbMatcher
{
    // Popularity gap threshold: if the second-best candidate is within this fraction of the best,
    // the result is ambiguous and goes to review.
    private const double AmbiguityGapFraction = 0.05; // 5 %

    // Year tolerance: if the TMDB year differs from the query year by more than this, flag as mismatch.
    private const int YearToleranceYears = 1;

    // Per-scan deduplication cache: keyed on (title, language, year?, kind?) — stores successful matches only.
    // Full 4-tuple key avoids cache collisions between:
    //   • movies and TV shows with the same title (kind dimension)
    //   • shows with the same name in different years (year dimension)
    //   • localised searches for the same title in different languages (language dimension)
    private readonly ConcurrentDictionary<(string title, string language, int? year, MediaType? kind), TmdbMatchResult>
        _cache = new();

    private readonly ILogger<TmdbMatcher> _logger;
    private readonly ITmdbService _tmdb;

    public TmdbMatcher(ITmdbService tmdb, ILogger<TmdbMatcher>? logger = null)
    {
        _tmdb = tmdb;
        _logger = logger ?? NullLogger<TmdbMatcher>.Instance;
    }

    /// <inheritdoc />
    public async Task<TmdbMatchResult> ResolveAsync(MatchQuery query, CancellationToken ct = default)
    {
        try
        {
            return await ResolveInternalAsync(query, ct);
        }
        catch (HttpRequestException ex)
        {
            // FR-017: transient failures must not abort the scan run
            _logger.LogWarning(ex,
                "Transient TMDB failure while resolving '{Title}' ({Year}). Routing to review queue.",
                query.Title, query.Year);

            return NeedsReview(ReviewReason.NoTmdbResult, []);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Unexpected error while resolving '{Title}' ({Year}) via TMDB. Routing to review queue.",
                query.Title, query.Year);

            return NeedsReview(ReviewReason.NoTmdbResult, []);
        }
    }

    // =========================================================================
    // Internal resolution — applies the full precedence chain
    // =========================================================================

    private async Task<TmdbMatchResult> ResolveInternalAsync(MatchQuery query, CancellationToken ct)
    {
        // ── Step 1: NfoTmdbId (highest precedence) ────────────────────────────
        if (query.NfoTmdbId.HasValue)
        {
            _logger.LogDebug("TMDB: resolving by NfoTmdbId={Id}", query.NfoTmdbId.Value);
            var result = await LookupByIdAsync(query.NfoTmdbId.Value, query.KindHint, ct);
            if (result is not null) return result;

            return NeedsReview(ReviewReason.NoTmdbResult, []);
        }

        // ── Step 2: ExplicitTokenId (second precedence) ───────────────────────
        if (query.ExplicitTokenId.HasValue)
        {
            _logger.LogDebug("TMDB: resolving by ExplicitTokenId={Id}", query.ExplicitTokenId.Value);
            var result = await LookupByIdAsync(query.ExplicitTokenId.Value, query.KindHint, ct);
            if (result is not null) return result;

            return NeedsReview(ReviewReason.NoTmdbResult, []);
        }

        // ── Steps 3+4: Multi-language title search ───────────────────────────
        // When SearchLanguages is set, iterate the full list. Otherwise fall back to the
        // legacy Language field (defaults to "en-US") for backward compatibility.
        var languages = query.SearchLanguages ?? [query.Language];

        TmdbMatchResult? lastFailResult = null;

        // Primary title — try in each language
        foreach (var lang in languages)
        {
            var cacheKey = (query.Title, lang, query.Year, query.KindHint);
            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                _logger.LogDebug("TMDB: cache hit for '{Title}' (lang={Lang}, year={Year})",
                    query.Title, lang, query.Year);
                return cached;
            }

            var result = await TryResolveByTitleAsync(query with { Language = lang }, ct);
            if (result is { NeedsReview: false })
            {
                _cache[cacheKey] = result;
                return result;
            }

            lastFailResult = result;
        }

        // ── Step 5: FallbackTitle retry ───────────────────────────────────────
        // Only when FallbackTitle is set AND differs from the primary title.
        // Callers must ensure FallbackTitle != Title to avoid a duplicate API call (F2 guard).
        if (query.FallbackTitle is not null && query.FallbackTitle != query.Title)
        {
            foreach (var lang in languages)
            {
                var cacheKey = (query.FallbackTitle, lang, (int?)null, query.KindHint);
                if (_cache.TryGetValue(cacheKey, out var cached))
                {
                    _logger.LogDebug("TMDB: cache hit for FallbackTitle '{Title}' (lang={Lang})",
                        query.FallbackTitle, lang);
                    return cached;
                }

                // FallbackTitle searches are year-agnostic: the folder title may not carry
                // the same year disambiguation as the filename-derived title.
                var fallbackQuery = query with { Title = query.FallbackTitle, Year = null, Language = lang };
                var result = await TryResolveByTitleAsync(fallbackQuery, ct);
                if (result is { NeedsReview: false })
                {
                    _cache[cacheKey] = result;
                    return result;
                }

                lastFailResult = result;
            }
        }

        // All language × title combinations exhausted — return the last failure reason so that
        // YearMismatch / MultipleCandidates reasons surface correctly on single-language queries.
        return lastFailResult ?? NeedsReview(ReviewReason.NoTmdbResult, []);
    }

    // =========================================================================
    // Id-based lookup — tries movie first, then TV show
    // =========================================================================

    private async Task<TmdbMatchResult?> LookupByIdAsync(int tmdbId, MediaType? kindHint, CancellationToken ct)
    {
        // Prefer the hinted kind; fall back to trying both
        if (kindHint == MediaType.TvShow)
        {
            var tv = await _tmdb.GetTvShowByIdAsync(tmdbId, cancellationToken: ct);
            if (tv is not null) return Matched(tv);
            var movie = await _tmdb.GetMovieByIdAsync(tmdbId, cancellationToken: ct);
            if (movie is not null) return Matched(movie);
        }
        else
        {
            var movie = await _tmdb.GetMovieByIdAsync(tmdbId, cancellationToken: ct);
            if (movie is not null) return Matched(movie);
            var tv = await _tmdb.GetTvShowByIdAsync(tmdbId, cancellationToken: ct);
            if (tv is not null) return Matched(tv);
        }

        return null;
    }

    // =========================================================================
    // Title-based resolution — year+title first, then title-only fallback
    // =========================================================================

    /// <summary>
    ///     Attempts to resolve <paramref name="query" /> by title (+year if provided).
    ///     Uses the year+title search when <see cref="MatchQuery.Year" /> is set, falling back to
    ///     title-only when the year search returns zero candidates. Always applies
    ///     <see cref="ApplyPolicy" /> to the candidate list before returning.
    /// </summary>
    private async Task<TmdbMatchResult> TryResolveByTitleAsync(MatchQuery query, CancellationToken ct)
    {
        if (query.Year.HasValue)
        {
            var yearCandidates = await _tmdb.SearchCandidatesAsync(
                query.Title, query.Year, query.KindHint, query.Language, ct);

            if (yearCandidates.Count > 0)
                return ApplyPolicy(yearCandidates, query.Year);

            // Year search returned nothing — try title-only as fallback
        }

        var candidates = await _tmdb.SearchCandidatesAsync(
            query.Title, null, query.KindHint, query.Language, ct);

        return candidates.Count > 0
            ? ApplyPolicy(candidates, query.Year)
            : NeedsReview(ReviewReason.NoTmdbResult, []);
    }

    // =========================================================================
    // Ambiguity + year policy
    // =========================================================================

    private TmdbMatchResult ApplyPolicy(IReadOnlyList<TmdbSearchCandidate> candidates, int? queryYear)
    {
        if (candidates.Count == 0) return NeedsReview(ReviewReason.NoTmdbResult, []);

        var ordered = candidates.OrderByDescending(c => c.PopularityScore).ToList();
        var best = ordered[0];

        // Ambiguity check: is the second-best within 5 % of the best?
        if (ordered.Count >= 2)
        {
            var second = ordered[1];
            if (best.PopularityScore > 0)
            {
                var gap = (double)(best.PopularityScore - second.PopularityScore) / (double)best.PopularityScore;
                if (gap <= AmbiguityGapFraction)
                {
                    // Too close to call → needs human review
                    _logger.LogDebug(
                        "TMDB: ambiguous candidates for '{Title}' — top scores {S1} vs {S2} (gap {G:P1})",
                        best.Title, best.PopularityScore, second.PopularityScore, gap);

                    return NeedsReview(ReviewReason.MultipleCandidates, candidates);
                }
            }
        }

        // Year mismatch check
        if (queryYear.HasValue && best.Year.HasValue)
        {
            var yearDiff = Math.Abs(best.Year.Value - queryYear.Value);
            if (yearDiff > YearToleranceYears)
            {
                _logger.LogDebug(
                    "TMDB: year mismatch for '{Title}' — query {QY} vs result {RY}",
                    best.Title, queryYear.Value, best.Year.Value);

                return NeedsReview(ReviewReason.YearMismatch, candidates);
            }
        }

        return new TmdbMatchResult(
            true,
            best.TmdbId,
            best.Kind,
            false,
            null,
            ToTmdbCandidates(candidates));
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static TmdbMatchResult Matched(TmdbIdLookupResult lookup)
    {
        return new TmdbMatchResult(true,
            lookup.TmdbId,
            lookup.Kind,
            false,
            null,
            []);
    }

    private static TmdbMatchResult NeedsReview(ReviewReason reason, IReadOnlyList<TmdbSearchCandidate> candidates)
    {
        return new TmdbMatchResult(false,
            null,
            null,
            true,
            reason,
            ToTmdbCandidates(candidates));
    }

    private static IReadOnlyList<TmdbCandidate> ToTmdbCandidates(IReadOnlyList<TmdbSearchCandidate> src)
    {
        return src.Select(c => new TmdbCandidate(c.TmdbId, c.Kind, c.Title, c.Year, c.PopularityScore, c.PosterPath))
            .ToList();
    }
}

