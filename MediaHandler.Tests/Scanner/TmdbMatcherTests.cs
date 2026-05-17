// TmdbMatcherTests — unit tests for the TMDB precedence-chain resolver.
// These tests must FAIL before TmdbMatcher.cs is implemented.

using FluentAssertions;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models.Scanner;
using MediaHandler.Domain.Enums;
using MediaHandler.Infrastructure.Nas.Scanner;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace MediaHandler.Tests.Scanner;

/// <summary>
///     Unit tests for <c>TmdbMatcher</c>.
///     Covers: precedence chain, ambiguity policy, year-mismatch policy, transient error tolerance.
/// </summary>
public class TmdbMatcherTests
{
    // =========================================================================
    // Test fixture helpers
    // =========================================================================

    private static ITmdbService CreateService()
    {
        return Substitute.For<ITmdbService>();
    }

    private static TmdbMatcher CreateMatcher(ITmdbService service)
    {
        return new TmdbMatcher(service);
    }

    private static TmdbSearchCandidate Movie(int id, string title, int year, double popularity = 50.0)
    {
        return new TmdbSearchCandidate(id, MediaType.Film, title, year, (decimal)popularity, null);
    }

    private static TmdbSearchCandidate TvShow(int id, string title, int year, double popularity = 50.0)
    {
        return new TmdbSearchCandidate(id, MediaType.TvShow, title, year, (decimal)popularity, null);
    }

    // =========================================================================
    // Precedence: NfoTmdbId wins
    // =========================================================================

    /// <remarks>
    ///     SOURCE: tasks.md spec — NfoTmdbId has highest precedence in the resolution chain.
    /// </remarks>
    [Fact]
    public async Task ResolveAsync_NfoTmdbId_WinsOverTitleYear()
    {
        var service = CreateService();
        service.GetMovieByIdAsync(12345, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new TmdbIdLookupResult(12345, MediaType.Film, "Inception", 2010, null));

        // Title+year stub — should NOT be called
        service.SearchCandidatesAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<MediaType?>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var matcher = CreateMatcher(service);
        var query = new MatchQuery("Some Other Movie", 2000, MediaType.Film, 12345);

        var result = await matcher.ResolveAsync(query, TestContext.Current.CancellationToken);

        result.IsMatched.Should().BeTrue();
        result.TmdbId.Should().Be(12345);
        result.NeedsReview.Should().BeFalse();

        // Verify no title search was performed
        await service.DidNotReceive().SearchCandidatesAsync(
            Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<MediaType?>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // =========================================================================
    // Precedence: ExplicitTokenId wins over title+year
    // =========================================================================

    /// <remarks>
    ///     SOURCE: tasks.md spec — ExplicitTokenId (e.g., {tmdbid=12345} in filename) is second precedence.
    /// </remarks>
    [Fact]
    public async Task ResolveAsync_ExplicitTokenId_WinsOverTitleYear()
    {
        var service = CreateService();
        service.GetMovieByIdAsync(99999, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new TmdbIdLookupResult(99999, MediaType.Film, "Avatar", 2009, null));

        var matcher = CreateMatcher(service);
        var query = new MatchQuery("Noisy.Release.2009.BluRay", 2009, MediaType.Film,
            null, 99999);

        var result = await matcher.ResolveAsync(query, TestContext.Current.CancellationToken);

        result.IsMatched.Should().BeTrue();
        result.TmdbId.Should().Be(99999);
        result.NeedsReview.Should().BeFalse();

        await service.DidNotReceive().SearchCandidatesAsync(
            Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<MediaType?>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // =========================================================================
    // Precedence: title+year wins over title-only
    // =========================================================================

    /// <remarks>
    ///     SOURCE: tasks.md spec — Title+Year search should be tried before Title-only.
    ///     When a single candidate is returned for title+year, it is accepted.
    /// </remarks>
    [Fact]
    public async Task ResolveAsync_TitleAndYear_ReturnsMatch_WhenSingleCandidate()
    {
        var service = CreateService();
        service.SearchCandidatesAsync("Inception", 2010, MediaType.Film, Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns([Movie(27205, "Inception", 2010, 80)]);

        var matcher = CreateMatcher(service);
        var query = new MatchQuery("Inception", 2010, MediaType.Film);

        var result = await matcher.ResolveAsync(query, TestContext.Current.CancellationToken);

        result.IsMatched.Should().BeTrue();
        result.TmdbId.Should().Be(27205);
        result.NeedsReview.Should().BeFalse();
    }

    // =========================================================================
    // Ambiguity: multiple candidates within 5% popularity → MultipleCandidates
    // =========================================================================

    /// <remarks>
    ///     ≥ 2 candidates within 5% popularity gap → MultipleCandidates review reason.
    /// </remarks>
    [Fact]
    public async Task ResolveAsync_MultipleCandidatesWithinPopularityGap_ReturnsNeedsReview()
    {
        var service = CreateService();

        // Both candidates have similar popularity — within 5%
        service.SearchCandidatesAsync("The Fly", 1986, Arg.Any<MediaType?>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns([
                Movie(10001, "The Fly", 1986, 60.0),
                Movie(10002, "The Fly", 1986, 58.0) // within 5% of 60
            ]);

        var matcher = CreateMatcher(service);
        var query = new MatchQuery("The Fly", 1986, null);

        var result = await matcher.ResolveAsync(query, TestContext.Current.CancellationToken);

        result.NeedsReview.Should().BeTrue();
        result.ReviewReason.Should().Be(ReviewReason.MultipleCandidates);
        result.Candidates.Should().HaveCountGreaterThanOrEqualTo(2);
        result.IsMatched.Should().BeFalse();
    }

    // =========================================================================
    // Year mismatch beyond ±1 → YearMismatch
    // =========================================================================

    /// <remarks>
    ///     Year mismatch &gt; 1 → YearMismatch review reason.
    /// </remarks>
    [Fact]
    public async Task ResolveAsync_YearMismatch_BeyondOneTolerance_ReturnsNeedsReview()
    {
        var service = CreateService();

        // Query year = 2010, result year = 2014 → 4-year mismatch → needs review
        service.SearchCandidatesAsync("Inception", 2010, Arg.Any<MediaType?>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns([Movie(27205, "Inception", 2014, 80)]);

        var matcher = CreateMatcher(service);
        var query = new MatchQuery("Inception", 2010, MediaType.Film);

        var result = await matcher.ResolveAsync(query, TestContext.Current.CancellationToken);

        result.NeedsReview.Should().BeTrue();
        result.ReviewReason.Should().Be(ReviewReason.YearMismatch);
    }

    // =========================================================================
    // Year within tolerance (±1) is accepted
    // =========================================================================

    [Theory]
    [InlineData(2009, 2010)] // one year before release date
    [InlineData(2011, 2010)] // one year after release date
    [InlineData(2010, 2010)] // exact match
    public async Task ResolveAsync_YearWithinOneTolerance_IsAccepted(int queryYear, int tmdbYear)
    {
        var service = CreateService();
        service.SearchCandidatesAsync(Arg.Any<string>(), queryYear, Arg.Any<MediaType?>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns([Movie(27205, "Inception", tmdbYear, 80)]);

        var matcher = CreateMatcher(service);
        var query = new MatchQuery("Inception", queryYear, MediaType.Film);

        var result = await matcher.ResolveAsync(query, TestContext.Current.CancellationToken);

        result.NeedsReview.Should().BeFalse();
        result.IsMatched.Should().BeTrue();
    }

    // =========================================================================
    // No result → NoTmdbResult
    // =========================================================================

    /// <remarks>
    ///     Empty candidate list → NoTmdbResult review reason.
    /// </remarks>
    [Fact]
    public async Task ResolveAsync_NoResults_ReturnsNeedsReview_WithNoTmdbResultReason()
    {
        var service = CreateService();
        service.SearchCandidatesAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<MediaType?>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var matcher = CreateMatcher(service);
        var query = new MatchQuery("ZzzzUnknownTitle9999", 1900, MediaType.Film);

        var result = await matcher.ResolveAsync(query, TestContext.Current.CancellationToken);

        result.NeedsReview.Should().BeTrue();
        result.ReviewReason.Should().Be(ReviewReason.NoTmdbResult);
        result.IsMatched.Should().BeFalse();
    }

    // =========================================================================
    // Transient HTTP failure → Result with NeedsReview, no throw
    // =========================================================================

    /// <remarks>
    ///     Transient HTTP errors are surfaced as NeedsReview without aborting the run.
    /// </remarks>
    [Fact]
    public async Task ResolveAsync_TransientHttpFailure_DoesNotThrow_ReturnsNeedsReview()
    {
        var service = CreateService();
        service.SearchCandidatesAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<MediaType?>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection timed out"));

        var matcher = CreateMatcher(service);
        var query = new MatchQuery("Inception", 2010, MediaType.Film);

        // Should not throw
        var act = async () => await matcher.ResolveAsync(query, TestContext.Current.CancellationToken);
        await act.Should().NotThrowAsync();

        var result = await matcher.ResolveAsync(query, TestContext.Current.CancellationToken);
        result.NeedsReview.Should().BeTrue();
        result.IsMatched.Should().BeFalse();
    }

    // =========================================================================
    // NfoTmdbId lookup failure (TMDB API returns null) → NoTmdbResult, not throw
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_NfoTmdbId_LookupReturnNull_ReturnsNeedsReview()
    {
        var service = CreateService();
        service.GetMovieByIdAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((TmdbIdLookupResult?)null);
        service.GetTvShowByIdAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((TmdbIdLookupResult?)null);

        var matcher = CreateMatcher(service);
        var query = new MatchQuery("Unknown", 2010, MediaType.Film, 999999);

        var result = await matcher.ResolveAsync(query, TestContext.Current.CancellationToken);

        result.NeedsReview.Should().BeTrue();
        result.ReviewReason.Should().Be(ReviewReason.NoTmdbResult);
    }

    // =========================================================================
    // LRU cache: same query returns cached result (service called only once)
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_SameQuery_UsesCachedResult()
    {
        var service = CreateService();
        service.SearchCandidatesAsync("Inception", 2010, MediaType.Film, Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns([Movie(27205, "Inception", 2010, 80)]);

        var matcher = CreateMatcher(service);
        var query = new MatchQuery("Inception", 2010, MediaType.Film);

        await matcher.ResolveAsync(query, TestContext.Current.CancellationToken);
        await matcher.ResolveAsync(query, TestContext.Current.CancellationToken); // second call should hit cache

        await service.Received(1).SearchCandidatesAsync(
            "Inception", 2010, MediaType.Film, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // =========================================================================
    // Candidates within 5% vs outside 5% (boundary tests)
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_SecondCandidateJustOutside5PercentGap_ReturnsWinner()
    {
        var service = CreateService();

        // Scores: 100 and 93 → gap is 7% → outside 5% tolerance → highest score wins
        service.SearchCandidatesAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<MediaType?>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([
                Movie(27205, "Inception", 2010, 100.0),
                Movie(12345, "Inception II", 2009, 93.0)
            ]);

        var matcher = CreateMatcher(service);
        var query = new MatchQuery("Inception", 2010, MediaType.Film);

        var result = await matcher.ResolveAsync(query, TestContext.Current.CancellationToken);

        result.IsMatched.Should().BeTrue();
        result.NeedsReview.Should().BeFalse();
        result.TmdbId.Should().Be(27205); // highest-score candidate wins
    }

    // =========================================================================
    // Title-only fallback when no title+year result
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_TitleYearEmpty_FallsBackToTitleOnly()
    {
        var service = CreateService();

        // title+year returns nothing
        service.SearchCandidatesAsync("Inception", 2010, Arg.Any<MediaType?>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns([]);

        // title-only returns one candidate
        service.SearchCandidatesAsync("Inception", null, Arg.Any<MediaType?>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns([Movie(27205, "Inception", 2010, 80)]);

        var matcher = CreateMatcher(service);
        var query = new MatchQuery("Inception", 2010, MediaType.Film);

        var result = await matcher.ResolveAsync(query, TestContext.Current.CancellationToken);

        result.IsMatched.Should().BeTrue();
        result.TmdbId.Should().Be(27205);
    }

    // =========================================================================
    // Override-precedence — NFO id always wins
    // SOURCE: plan.md — NfoTmdbId precedes all other resolution signals.
    // =========================================================================

    /// <remarks>
    ///     When both an NFO id and an explicit filename token id are present, the NFO id wins.
    ///     Full precedence chain: NfoTmdbId &gt; ExplicitTokenId &gt; Title+Year &gt; Title.
    /// </remarks>
    [Fact]
    public async Task ResolveAsync_NfoTmdbId_WinsOverExplicitTokenId()
    {
        var service = CreateService();

        // NFO id = 27205 (Inception)
        service.GetMovieByIdAsync(27205, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new TmdbIdLookupResult(27205, MediaType.Film, "Inception", 2010, null));

        // Explicit token id = 99999 — should NOT be resolved
        service.GetMovieByIdAsync(99999, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new TmdbIdLookupResult(99999, MediaType.Film, "SomeOtherMovie", 2015, null));

        var matcher = CreateMatcher(service);
        // Both NfoTmdbId and ExplicitTokenId set — NFO wins
        var query = new MatchQuery("Inception", 2010, MediaType.Film,
            27205, 99999);

        var result = await matcher.ResolveAsync(query, TestContext.Current.CancellationToken);

        result.IsMatched.Should().BeTrue();
        result.TmdbId.Should().Be(27205, "NfoTmdbId must take precedence over ExplicitTokenId");
        result.NeedsReview.Should().BeFalse();

        // ExplicitTokenId lookup must NOT be called
        await service.DidNotReceive().GetMovieByIdAsync(99999, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <remarks>
    ///     When an NFO id is present, no title-based TMDB search is performed at all.
    ///     This guarantees the NFO is the definitive override regardless of how well the filename parses.
    /// </remarks>
    [Fact]
    public async Task ResolveAsync_NfoTmdbId_WinsOverTitleYearSearch_NoSearchCalled()
    {
        var service = CreateService();

        service.GetMovieByIdAsync(27205, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new TmdbIdLookupResult(27205, MediaType.Film, "Inception", 2010, null));

        var matcher = CreateMatcher(service);
        // NfoTmdbId given alongside a perfectly valid title+year
        var query = new MatchQuery("Inception", 2010, MediaType.Film, 27205);

        var result = await matcher.ResolveAsync(query, TestContext.Current.CancellationToken);

        result.IsMatched.Should().BeTrue();
        result.TmdbId.Should().Be(27205);

        // Title search must never run when NfoTmdbId resolves successfully
        await service.DidNotReceive().SearchCandidatesAsync(
            Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<MediaType?>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <remarks>
    ///     An NFO-provided tmdbid that does not exist on TMDB (lookup returns null for both
    ///     movie and TV show) must produce NeedsReview, not fall through to title+year search.
    ///     This protects against stale or incorrect NFO ids silently picking the wrong item.
    /// </remarks>
    [Fact]
    public async Task ResolveAsync_NfoTmdbId_NotFoundOnTmdb_ReturnsNeedsReview_WithoutTitleFallback()
    {
        var service = CreateService();

        // NFO id not found on TMDB
        service.GetMovieByIdAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((TmdbIdLookupResult?)null);
        service.GetTvShowByIdAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((TmdbIdLookupResult?)null);

        var matcher = CreateMatcher(service);
        var query = new MatchQuery("Inception", 2010, MediaType.Film, 99999);

        var result = await matcher.ResolveAsync(query, TestContext.Current.CancellationToken);

        result.NeedsReview.Should().BeTrue();
        result.IsMatched.Should().BeFalse();

        // Title fallback must NOT be attempted when NfoTmdbId is set
        await service.DidNotReceive().SearchCandidatesAsync(
            Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<MediaType?>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // =========================================================================
    // Multi-language TMDB search
    // SOURCE: contracts/internal-contracts.md — ITmdbMatcher.ResolveAsync updated chain
    // =========================================================================

    /// <summary>
    ///     Primary language match: first language in SearchLanguages resolves → second never called.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_MultiLanguage_PrimaryLanguageMatches_SecondLanguageNotCalled()
    {
        var service = CreateService();

        // fr-FR returns a match
        service.SearchCandidatesAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<MediaType?>(),
                "fr-FR", Arg.Any<CancellationToken>())
            .Returns([TvShow(12345, "Une Nounou D'enfer", 1993, 80)]);

        // en-US should NOT be called
        service.SearchCandidatesAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<MediaType?>(),
                "en-US", Arg.Any<CancellationToken>())
            .Returns([TvShow(12345, "The Nanny", 1993, 80)]);

        var matcher = CreateMatcher(service);
        var query = new MatchQuery(
            "Une Nounou D'enfer", null, MediaType.TvShow,
            SearchLanguages: ["fr-FR", "en-US"]);

        var result = await matcher.ResolveAsync(query, TestContext.Current.CancellationToken);

        result.IsMatched.Should().BeTrue("title found in first language should produce a match");
        result.NeedsReview.Should().BeFalse();

        // en-US must NOT have been called since fr-FR succeeded
        await service.DidNotReceive().SearchCandidatesAsync(
            Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<MediaType?>(),
            "en-US", Arg.Any<CancellationToken>());
    }

    /// <summary>
    ///     Fallback language match: primary language returns nothing; second language finds it.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_MultiLanguage_FallbackLanguageMatches()
    {
        var service = CreateService();

        // fr-FR returns nothing
        service.SearchCandidatesAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<MediaType?>(),
                "fr-FR", Arg.Any<CancellationToken>())
            .Returns([]);

        // en-US returns the canonical title
        service.SearchCandidatesAsync("Sur écoute", Arg.Any<int?>(), Arg.Any<MediaType?>(),
                "en-US", Arg.Any<CancellationToken>())
            .Returns([TvShow(1438, "The Wire", 2002, 75)]);

        var matcher = CreateMatcher(service);
        var query = new MatchQuery(
            "Sur écoute", null, MediaType.TvShow,
            SearchLanguages: ["fr-FR", "en-US"]);

        var result = await matcher.ResolveAsync(query, TestContext.Current.CancellationToken);

        result.IsMatched.Should().BeTrue("second-language search should succeed when first is empty");
        result.NeedsReview.Should().BeFalse();
        result.TmdbId.Should().Be(1438);
    }

    /// <summary>
    ///     FallbackTitle retry: primary title exhausts ALL languages, then FallbackTitle matches.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_MultiLanguage_FallbackTitleRetried_AfterPrimaryExhausted()
    {
        var service = CreateService();

        // Primary title ("Une Nounou Denfer") finds nothing in any language
        service.SearchCandidatesAsync("Une Nounou Denfer", Arg.Any<int?>(), Arg.Any<MediaType?>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([]);

        // FallbackTitle ("The Nanny") finds the correct show
        service.SearchCandidatesAsync("The Nanny", Arg.Any<int?>(), Arg.Any<MediaType?>(),
                "en-US", Arg.Any<CancellationToken>())
            .Returns([TvShow(2734, "The Nanny", 1993, 78)]);

        var matcher = CreateMatcher(service);
        var query = new MatchQuery(
            "Une Nounou Denfer", null, MediaType.TvShow,
            FallbackTitle: "The Nanny",
            SearchLanguages: ["fr-FR", "en-US"]);

        var result = await matcher.ResolveAsync(query, TestContext.Current.CancellationToken);

        result.IsMatched.Should().BeTrue("FallbackTitle should be retried after primary title is exhausted");
        result.NeedsReview.Should().BeFalse();
        result.TmdbId.Should().Be(2734);
    }

    /// <summary>
    ///     FallbackTitle == Title guard: when FallbackTitle equals Title, no extra TMDB call is issued.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_FallbackTitleEqualsTitle_NoDuplicateCallIssued()
    {
        var service = CreateService();

        // Either title returns nothing → NeedsReview
        service.SearchCandidatesAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<MediaType?>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var matcher = CreateMatcher(service);
        var query = new MatchQuery(
            "The Wire", null, MediaType.TvShow,
            FallbackTitle: "The Wire",       // FallbackTitle == Title → guard should prevent duplicate
            SearchLanguages: ["en-US"]);

        await matcher.ResolveAsync(query, TestContext.Current.CancellationToken);

        // Only one language × one title = 1 SearchCandidatesAsync call (no FallbackTitle retry)
        await service.Received(1).SearchCandidatesAsync(
            "The Wire", Arg.Any<int?>(), Arg.Any<MediaType?>(),
            "en-US", Arg.Any<CancellationToken>());
    }

    /// <summary>
    ///     Deduplication: same (title, language, year, kind) not called twice in same matcher instance.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_MultiLanguage_SameQueryWithLanguage_UsesCachedResult()
    {
        var service = CreateService();
        service.SearchCandidatesAsync("Breaking Bad", null, MediaType.TvShow, "en-US",
                Arg.Any<CancellationToken>())
            .Returns([TvShow(1396, "Breaking Bad", 2008, 90)]);

        var matcher = CreateMatcher(service);
        var query = new MatchQuery(
            "Breaking Bad", null, MediaType.TvShow,
            SearchLanguages: ["en-US"]);

        await matcher.ResolveAsync(query, TestContext.Current.CancellationToken);
        await matcher.ResolveAsync(query, TestContext.Current.CancellationToken); // cache hit

        // Service called only once despite two resolver calls
        await service.Received(1).SearchCandidatesAsync(
            "Breaking Bad", Arg.Any<int?>(), Arg.Any<MediaType?>(),
            "en-US", Arg.Any<CancellationToken>());
    }

    /// <summary>
    ///     NeedsReview: all languages and FallbackTitle exhausted → NeedsReview returned.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_MultiLanguage_AllExhausted_ReturnsNeedsReview()
    {
        var service = CreateService();

        // Nothing found under any language or title
        service.SearchCandidatesAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<MediaType?>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var matcher = CreateMatcher(service);
        var query = new MatchQuery(
            "UnknownPrimary", null, MediaType.TvShow,
            FallbackTitle: "UnknownFallback",
            SearchLanguages: ["fr-FR", "en-US"]);

        var result = await matcher.ResolveAsync(query, TestContext.Current.CancellationToken);

        result.NeedsReview.Should().BeTrue("all language + FallbackTitle attempts exhausted → NeedsReview");
        result.IsMatched.Should().BeFalse();
    }
}