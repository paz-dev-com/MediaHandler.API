#nullable enable
// TmdbMatcher — resolves parsed file metadata to a TMDB entry using the precedence chain:
//   NfoTmdbId → ExplicitTokenId → Title+Year → Title
// Maintains an in-process LRU cache and applies the ambiguity / year-mismatch policy.
// Transient HTTP failures are caught and surfaced as NeedsReview without aborting the scan.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models.Scanner;
using MediaHandler.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaHandler.Infrastructure.Nas.Scanner;

/// <summary>
/// Production implementation of <see cref="ITmdbMatcher"/>.
/// Wraps <see cref="ITmdbService"/> and applies:
/// <list type="bullet">
///   <item>Precedence chain: NfoTmdbId → ExplicitTokenId → Title+Year → Title</item>
///   <item>Ambiguity policy: ≥ 2 candidates within 5 % popularity gap → <see cref="ReviewReason.MultipleCandidates"/></item>
///   <item>Year tolerance: mismatch &gt; ±1 → <see cref="ReviewReason.YearMismatch"/></item>
///   <item>LRU cache keyed on <c>(title, year, kind)</c> — max 1,000 entries per scan instance</item>
///   <item>Transient error tolerance: <see cref="HttpRequestException"/> caught, result surfaced as NeedsReview</item>
/// </list>
/// </summary>
public sealed class TmdbMatcher : ITmdbMatcher
{
    private readonly ITmdbService _tmdb;
    private readonly ILogger<TmdbMatcher> _logger;

    // Popularity gap threshold: if the second-best candidate is within this fraction of the best,
    // the result is ambiguous and goes to review.
    private const double AmbiguityGapFraction = 0.05; // 5 %

    // Year tolerance: if the TMDB year differs from the query year by more than this, flag as mismatch.
    private const int YearToleranceYears = 1;

    // LRU cache: keyed on (title, year, kind) — stores the final TmdbMatchResult
    private readonly LruCache<(string title, int? year, MediaType? kind), TmdbMatchResult> _cache;

    public TmdbMatcher(ITmdbService tmdb, ILogger<TmdbMatcher>? logger = null)
    {
        _tmdb = tmdb;
        _logger = logger ?? NullLogger<TmdbMatcher>.Instance;
        _cache = new LruCache<(string, int?, MediaType?), TmdbMatchResult>(capacity: 1_000);
    }

    /// <inheritdoc/>
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

        // ── Steps 3+4: Title+Year → Title ────────────────────────────────────
        var cacheKey = (query.Title, query.Year, query.KindHint);
        if (_cache.TryGet(cacheKey, out var cached))
        {
            _logger.LogDebug("TMDB: cache hit for '{Title}' ({Year})", query.Title, query.Year);
            return cached!;
        }

        // Try Title+Year first
        TmdbMatchResult matchResult;
        if (query.Year.HasValue)
        {
            var yearCandidates = await _tmdb.SearchCandidatesAsync(
                query.Title, query.Year, query.KindHint, query.Language, ct);

            matchResult = yearCandidates.Count > 0
                ? ApplyPolicy(yearCandidates, query.Year)
                : await TryTitleOnlyAsync(query, ct);
        }
        else
        {
            matchResult = await TryTitleOnlyAsync(query, ct);
        }

        _cache.Set(cacheKey, matchResult);
        return matchResult;
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
    // Title-only fallback
    // =========================================================================

    private async Task<TmdbMatchResult> TryTitleOnlyAsync(MatchQuery query, CancellationToken ct)
    {
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
            IsMatched: true,
            TmdbId: best.TmdbId,
            Kind: best.Kind,
            NeedsReview: false,
            ReviewReason: null,
            Candidates: ToTmdbCandidates(candidates));
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static TmdbMatchResult Matched(TmdbIdLookupResult lookup) =>
        new(IsMatched: true,
            TmdbId: lookup.TmdbId,
            Kind: lookup.Kind,
            NeedsReview: false,
            ReviewReason: null,
            Candidates: []);

    private static TmdbMatchResult NeedsReview(ReviewReason reason, IReadOnlyList<TmdbSearchCandidate> candidates) =>
        new(IsMatched: false,
            TmdbId: null,
            Kind: null,
            NeedsReview: true,
            ReviewReason: reason,
            Candidates: ToTmdbCandidates(candidates));

    private static IReadOnlyList<TmdbCandidate> ToTmdbCandidates(IReadOnlyList<TmdbSearchCandidate> src) =>
        src.Select(c => new TmdbCandidate(c.TmdbId, c.Kind, c.Title, c.Year, c.PopularityScore, c.PosterPath))
           .ToList();

    // =========================================================================
    // Bounded LRU cache (simple linked-list + dictionary implementation)
    // =========================================================================

    private sealed class LruCache<TKey, TValue>(int capacity) where TKey : notnull
    {
        private readonly int _capacity = capacity;
        private readonly Dictionary<TKey, LinkedListNode<(TKey key, TValue value)>> _map = new();
        private readonly LinkedList<(TKey key, TValue value)> _list = new();

        public bool TryGet(TKey key, out TValue? value)
        {
            if (!_map.TryGetValue(key, out var node))
            {
                value = default;
                return false;
            }
            // Move to front (most recently used)
            _list.Remove(node);
            _list.AddFirst(node);
            value = node.Value.value;
            return true;
        }

        public void Set(TKey key, TValue value)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _list.Remove(existing);
                _map.Remove(key);
            }
            else if (_map.Count >= _capacity)
            {
                // Evict least recently used
                var last = _list.Last!;
                _map.Remove(last.Value.key);
                _list.RemoveLast();
            }

            var node = new LinkedListNode<(TKey, TValue)>((key, value));
            _list.AddFirst(node);
            _map[key] = node;
        }
    }
}

