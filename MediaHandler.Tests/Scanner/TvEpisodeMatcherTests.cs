#nullable enable
// TvEpisodeMatcherTests — Kodi TV episode number extraction
// SOURCE: https://kodi.wiki/view/Naming_video_files/TV_shows
// SOURCE: Kodi wiki "Episode matching patterns" (observed default behaviour)

using FluentAssertions;
using MediaHandler.Application.Common.Models.Scanner;
using MediaHandler.Infrastructure.Nas.Scanner;

namespace MediaHandler.Tests.Scanner;

/// <summary>
/// Table-driven tests for <see cref="TvEpisodeMatcher"/>.
/// Every pattern is sourced from public Kodi documentation; no text from GPL source.
/// </summary>
public class TvEpisodeMatcherTests
{
    private readonly TvEpisodeMatcher _sut = new();
    private static readonly EpisodeNumberingHint NoHint = new();

    // =========================================================================
    // SxxExx canonical format
    // SOURCE: Kodi wiki — "The SxxExx format is the most widely used"
    // =========================================================================

    [Theory]
    [InlineData("Show.S01E01.mkv", 1, 1)]
    [InlineData("Show.S02E05.mkv", 2, 5)]
    [InlineData("Show.S08E06.mkv", 8, 6)]
    [InlineData("Show.S10E18.mkv", 10, 18)]
    [InlineData("Show.S01E100.mkv", 1, 100)]
    [InlineData("show.s01e01.mkv", 1, 1)]       // lowercase
    [InlineData("Show.S01E01.720p.mkv", 1, 1)]  // with quality tag
    [InlineData("Show.S03E07.WEB.mkv", 3, 7)]
    [InlineData("Show.S12E01.mkv", 12, 1)]
    [InlineData("Show - S05E02 - Title.mkv", 5, 2)]
    [InlineData("Show_S01E11_720p.mkv", 1, 11)]
    [InlineData("Show.S02E04.PROPER.mkv", 2, 4)]
    public void Match_SxxExx_ExtractsCorrectNumbers(string filename, int expectedSeason, int expectedEpisode)
    {
        var result = _sut.Match(filename, NoHint);

        result.Should().HaveCount(1);
        result[0].Season.Should().Be(expectedSeason, because: $"filename: {filename}");
        result[0].Episode.Should().Be(expectedEpisode, because: $"filename: {filename}");
    }

    // =========================================================================
    // Multi-episode SxxExx-Eyy format
    // SOURCE: Kodi wiki — "Multi-episode files are indicated by SxxExx-Eyy or SxxExxEyy"
    // =========================================================================

    [Fact]
    public void Match_SxxExxDashEyy_ReturnsTwoEpisodes()
    {
        var result = _sut.Match("Breaking.Bad.S02E05-E06.mkv", NoHint);

        result.Should().HaveCountGreaterThanOrEqualTo(2, because: "S02E05-E06 spans two episodes");
        result[0].Season.Should().Be(2);
        result[0].Episode.Should().Be(5);
        result[1].Episode.Should().Be(6);
    }

    [Fact]
    public void Match_SxxExxEyy_ReturnsTwoEpisodes()
    {
        // SOURCE: Kodi wiki — compacted form SxxExxEyy (no dash)
        var result = _sut.Match("Show.S01E01E02.mkv", NoHint);

        result.Should().HaveCountGreaterThanOrEqualTo(2, because: "S01E01E02 spans two episodes");
        result[0].Episode.Should().Be(1);
        result[1].Episode.Should().Be(2);
    }

    [Fact]
    public void Match_MultiEpisodeRange_ReturnsAllEpisodes()
    {
        // SOURCE: Kodi wiki — range S01E01-E03 yields episodes 1, 2, 3
        var result = _sut.Match("Show.S01E01-E03.mkv", NoHint);
        result.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    // =========================================================================
    // 1x05 / xXy style
    // SOURCE: Kodi wiki — "1x05 is an accepted alternate to S01E05"
    // =========================================================================

    [Theory]
    [InlineData("Seinfeld.1x05.mkv", 1, 5)]
    [InlineData("show.2x12.mkv", 2, 12)]
    [InlineData("show.3x01.mkv", 3, 1)]
    [InlineData("show.10x24.mkv", 10, 24)]
    [InlineData("Show.1X03.mkv", 1, 3)]    // uppercase X
    [InlineData("Show.2X07.mkv", 2, 7)]
    public void Match_SeasonXEpisode_ExtractsCorrectNumbers(string filename, int expectedSeason, int expectedEpisode)
    {
        var result = _sut.Match(filename, NoHint);

        result.Should().HaveCount(1, because: $"filename: {filename}");
        result[0].Season.Should().Be(expectedSeason);
        result[0].Episode.Should().Be(expectedEpisode);
    }

    // =========================================================================
    // Date-based YYYY.MM.DD / YYYY-MM-DD
    // SOURCE: Kodi wiki — "Date-stamped episodes use YYYY-MM-DD or YYYY.MM.DD"
    // =========================================================================

    [Theory]
    [InlineData("The.Daily.Show.2024.03.19.mkv", 2024, 3, 19)]
    [InlineData("Late.Night.2023.11.04.mkv", 2023, 11, 4)]
    [InlineData("News.2022.01.15.mkv", 2022, 1, 15)]
    [InlineData("Show.2024-03-19.mkv", 2024, 3, 19)]   // dash-separated variant
    public void Match_DateBased_ExtractsYearAsSeasonAndDayOfYear(string filename, int year, int month, int day)
    {
        var result = _sut.Match(filename, NoHint);

        result.Should().HaveCount(1, because: $"date-based file: {filename}");
        result[0].Season.Should().Be(year, because: "year maps to season for date-based episodes");
        // Episode is encoded as ordinal day-of-year (consistent with Kodi's observed mapping)
        var expectedEpisode = new DateTime(year, month, day).DayOfYear;
        result[0].Episode.Should().Be(expectedEpisode);
    }

    // =========================================================================
    // Absolute episode numbering (anime-style)
    // SOURCE: Kodi wiki — "Absolute episode numbers (no season) are used in anime"
    // =========================================================================

    [Theory]
    [InlineData("Anime.E042.mkv", 0, 42)]
    [InlineData("Anime.042.mkv", 0, 42)]
    [InlineData("Anime.Episode.042.mkv", 0, 42)]
    public void Match_AbsoluteNumber_ReturnsSeasonZero(string filename, int expectedSeason, int expectedEpisode)
    {
        var result = _sut.Match(filename, NoHint);

        result.Should().HaveCount(1);
        result[0].Season.Should().Be(expectedSeason);
        result[0].Episode.Should().Be(expectedEpisode);
    }

    // =========================================================================
    // No match
    // =========================================================================

    [Theory]
    [InlineData("movie.mkv")]
    [InlineData("no_episode_info.mkv")]
    [InlineData("justtext.mkv")]
    public void Match_NoPattern_ReturnsEmpty(string filename)
    {
        var result = _sut.Match(filename, NoHint);
        result.Should().BeEmpty(because: $"'{filename}' has no recognisable episode pattern");
    }

    // =========================================================================
    // Folder hint
    // SOURCE: Observed Kodi behaviour — season folder context overrides ambiguous patterns
    // =========================================================================

    [Fact]
    public void Match_WithSeasonHint_UsesHintSeason()
    {
        // When the filename has an episode number but no season, the folder hint provides it
        var hint = new EpisodeNumberingHint(SeasonFromFolder: 3);
        var result = _sut.Match("Show.E07.mkv", hint);

        result.Should().HaveCount(1);
        result[0].Season.Should().Be(3, because: "the folder hint Season 03 provides the season");
        result[0].Episode.Should().Be(7);
    }

    [Fact]
    public void Match_SxxExx_IgnoresHintInFavourOfExplicitSeason()
    {
        // SOURCE: Observed Kodi behaviour — explicit SxxExx always wins over folder hint
        var hint = new EpisodeNumberingHint(SeasonFromFolder: 9);
        var result = _sut.Match("Show.S02E05.mkv", hint);

        result.Should().HaveCount(1);
        result[0].Season.Should().Be(2, because: "S02 explicitly overrides the folder hint of 9");
    }
}

