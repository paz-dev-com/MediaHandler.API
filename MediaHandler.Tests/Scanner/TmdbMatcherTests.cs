#nullable enable
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
/// Unit tests for <c>TmdbMatcher</c>.
/// Covers: precedence chain, ambiguity policy, year-mismatch policy, transient error tolerance.
/// </summary>
public class TmdbMatcherTests
{
    // =========================================================================
    // Test fixture helpers
    // =========================================================================

    private static ITmdbService CreateService() => Substitute.For<ITmdbService>();

    private static TmdbMatcher CreateMatcher(ITmdbService service) => new(service);

    private static TmdbSearchCandidate Movie(int id, string title, int year, double popularity = 50.0) =>
        new(id, MediaType.Film, title, year, (decimal)popularity, null);

    private static TmdbSearchCandidate TvShow(int id, string title, int year, double popularity = 50.0) =>
        new(id, MediaType.TvShow, title, year, (decimal)popularity, null);

    // =========================================================================
    // Precedence: NfoTmdbId wins
    // =========================================================================

    /// <remarks>
    /// SOURCE: tasks.md spec — NfoTmdbId has highest precedence in the resolution chain.
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
        var query = new MatchQuery("Some Other Movie", 2000, MediaType.Film, NfoTmdbId: 12345);

        var result = await matcher.ResolveAsync(query);

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
    /// SOURCE: tasks.md spec — ExplicitTokenId (e.g., {tmdbid=12345} in filename) is second precedence.
    /// </remarks>
    [Fact]
    public async Task ResolveAsync_ExplicitTokenId_WinsOverTitleYear()
    {
        var service = CreateService();
        service.GetMovieByIdAsync(99999, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new TmdbIdLookupResult(99999, MediaType.Film, "Avatar", 2009, null));

        var matcher = CreateMatcher(service);
        var query = new MatchQuery("Noisy.Release.2009.BluRay", 2009, MediaType.Film,
            NfoTmdbId: null, ExplicitTokenId: 99999);

        var result = await matcher.ResolveAsync(query);

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
    /// SOURCE: tasks.md spec — Title+Year search should be tried before Title-only.
    /// When a single candidate is returned for title+year, it is accepted.
    /// </remarks>
    [Fact]
    public async Task ResolveAsync_TitleAndYear_ReturnsMatch_WhenSingleCandidate()
    {
        var service = CreateService();
        service.SearchCandidatesAsync("Inception", 2010, MediaType.Film, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([Movie(27205, "Inception", 2010, 80)]);

        var matcher = CreateMatcher(service);
        var query = new MatchQuery("Inception", 2010, MediaType.Film);

        var result = await matcher.ResolveAsync(query);

        result.IsMatched.Should().BeTrue();
        result.TmdbId.Should().Be(27205);
        result.NeedsReview.Should().BeFalse();
    }

    // =========================================================================
    // Ambiguity: multiple candidates within 5% popularity → MultipleCandidates
    // =========================================================================

    /// <remarks>
    /// SOURCE: tasks.md T086 — ≥ 2 candidates within 5% popularity gap → MultipleCandidates review reason.
    /// </remarks>
    [Fact]
    public async Task ResolveAsync_MultipleCandidatesWithinPopularityGap_ReturnsNeedsReview()
    {
        var service = CreateService();

        // Both candidates have similar popularity — within 5%
        service.SearchCandidatesAsync("The Fly", 1986, Arg.Any<MediaType?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([
                Movie(10001, "The Fly", 1986, 60.0),
                Movie(10002, "The Fly", 1986, 58.0) // within 5% of 60
            ]);

        var matcher = CreateMatcher(service);
        var query = new MatchQuery("The Fly", 1986, null);

        var result = await matcher.ResolveAsync(query);

        result.NeedsReview.Should().BeTrue();
        result.ReviewReason.Should().Be(ReviewReason.MultipleCandidates);
        result.Candidates.Should().HaveCountGreaterThanOrEqualTo(2);
        result.IsMatched.Should().BeFalse();
    }

    // =========================================================================
    // Year mismatch beyond ±1 → YearMismatch
    // =========================================================================

    /// <remarks>
    /// SOURCE: tasks.md T086 — year mismatch > ±1 → YearMismatch review reason.
    /// </remarks>
    [Fact]
    public async Task ResolveAsync_YearMismatch_BeyondOneTolerance_ReturnsNeedsReview()
    {
        var service = CreateService();

        // Query year = 2010, result year = 2014 → 4-year mismatch → needs review
        service.SearchCandidatesAsync("Inception", 2010, Arg.Any<MediaType?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([Movie(27205, "Inception", 2014, 80)]);

        var matcher = CreateMatcher(service);
        var query = new MatchQuery("Inception", 2010, MediaType.Film);

        var result = await matcher.ResolveAsync(query);

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
        service.SearchCandidatesAsync(Arg.Any<string>(), queryYear, Arg.Any<MediaType?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([Movie(27205, "Inception", tmdbYear, 80)]);

        var matcher = CreateMatcher(service);
        var query = new MatchQuery("Inception", queryYear, MediaType.Film);

        var result = await matcher.ResolveAsync(query);

        result.NeedsReview.Should().BeFalse();
        result.IsMatched.Should().BeTrue();
    }

    // =========================================================================
    // No result → NoTmdbResult
    // =========================================================================

    /// <remarks>
    /// SOURCE: tasks.md T086 — empty candidate list → NoTmdbResult review reason.
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

        var result = await matcher.ResolveAsync(query);

        result.NeedsReview.Should().BeTrue();
        result.ReviewReason.Should().Be(ReviewReason.NoTmdbResult);
        result.IsMatched.Should().BeFalse();
    }

    // =========================================================================
    // Transient HTTP failure → Result with NeedsReview, no throw
    // =========================================================================

    /// <remarks>
    /// SOURCE: tasks.md T086 — transient HTTP errors surfaced as NeedsReview without aborting the run (FR-017).
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
        var act = async () => await matcher.ResolveAsync(query);
        await act.Should().NotThrowAsync();

        var result = await matcher.ResolveAsync(query);
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
        var query = new MatchQuery("Unknown", 2010, MediaType.Film, NfoTmdbId: 999999);

        var result = await matcher.ResolveAsync(query);

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
        service.SearchCandidatesAsync("Inception", 2010, MediaType.Film, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([Movie(27205, "Inception", 2010, 80)]);

        var matcher = CreateMatcher(service);
        var query = new MatchQuery("Inception", 2010, MediaType.Film);

        await matcher.ResolveAsync(query);
        await matcher.ResolveAsync(query); // second call should hit cache

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

        var result = await matcher.ResolveAsync(query);

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
        service.SearchCandidatesAsync("Inception", 2010, Arg.Any<MediaType?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([]);

        // title-only returns one candidate
        service.SearchCandidatesAsync("Inception", null, Arg.Any<MediaType?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([Movie(27205, "Inception", 2010, 80)]);

        var matcher = CreateMatcher(service);
        var query = new MatchQuery("Inception", 2010, MediaType.Film);

        var result = await matcher.ResolveAsync(query);

        result.IsMatched.Should().BeTrue();
        result.TmdbId.Should().Be(27205);
    }
}

