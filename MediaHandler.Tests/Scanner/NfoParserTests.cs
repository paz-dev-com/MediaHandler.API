#nullable enable
// Unit tests for the NFO sidecar file parser.
// These tests MUST fail before NfoParser.cs is implemented (T097).

using FluentAssertions;
using MediaHandler.Infrastructure.Nas.Scanner;

namespace MediaHandler.Tests.Scanner;

/// <summary>
/// Unit tests for <see cref="NfoParser"/>.
/// Covers: well-formed movie.nfo with tmdbid; malformed XML returns Malformed result (not throws);
/// missing optional fields return null without failing; tvshow.nfo and per-episode .nfo shapes.
///
/// Tests write temporary files to exercise the file-reading path of the parser.
/// </summary>
public class NfoParserTests : IAsyncLifetime
{
    private readonly NfoParser _sut = new();
    private readonly List<string> _tempFiles = [];

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        // Clean up temp files after each test class run
        foreach (var path in _tempFiles)
        {
            try { File.Delete(path); }
            catch { /* ignore cleanup errors */ }
        }

        await ValueTask.CompletedTask;
    }

    // =========================================================================
    // Helper
    // =========================================================================

    private async Task<string> WriteTempNfoAsync(string xml)
    {
        var path = Path.ChangeExtension(Path.GetTempFileName(), ".nfo");
        await File.WriteAllTextAsync(path, xml);
        _tempFiles.Add(path);
        return path;
    }

    // =========================================================================
    // Well-formed movie.nfo — all key fields present
    // SOURCE: Kodi wiki — https://kodi.wiki/view/NFO_files/Movies
    // =========================================================================

    /// <remarks>
    /// A well-formed movie.nfo with title, year and tmdbid must parse all three fields.
    /// </remarks>
    [Fact]
    public async Task ParseAsync_WellFormedMovieNfo_WithTmdbId_ReturnsParsedSuccessfully()
    {
        var xml = """
            <movie>
                <title>Inception</title>
                <year>2010</year>
                <tmdbid>27205</tmdbid>
            </movie>
            """;

        var path = await WriteTempNfoAsync(xml);
        var result = await _sut.ParseAsync(path);

        result.ParsedSuccessfully.Should().BeTrue();
        result.Title.Should().Be("Inception");
        result.Year.Should().Be(2010);
        result.TmdbId.Should().Be(27205);
        result.Warning.Should().BeNull();
    }

    /// <remarks>
    /// Additional fields (imdbid, plot) that Kodi writes are tolerated and ignored.
    /// SOURCE: Kodi wiki — NFO files contain many optional elements; scanners must not fail on unknown elements.
    /// </remarks>
    [Fact]
    public async Task ParseAsync_NfoWithUnknownElements_ParsesKnownFieldsAndIgnoresRest()
    {
        var xml = """
            <movie>
                <title>The Dark Knight</title>
                <year>2008</year>
                <tmdbid>155</tmdbid>
                <imdbid>tt0468569</imdbid>
                <plot>Some plot description</plot>
                <runtime>152</runtime>
                <rating>9.0</rating>
                <unknownfutureelement>data</unknownfutureelement>
            </movie>
            """;

        var path = await WriteTempNfoAsync(xml);
        var result = await _sut.ParseAsync(path);

        result.ParsedSuccessfully.Should().BeTrue();
        result.Title.Should().Be("The Dark Knight");
        result.Year.Should().Be(2008);
        result.TmdbId.Should().Be(155);
        result.ImdbId.Should().Be("tt0468569");
    }

    // =========================================================================
    // Malformed XML — must return Malformed result, never throw
    // SOURCE: tasks.md spec — malformed NFO returns NfoParseResult.Malformed (not throws)
    // =========================================================================

    [Fact]
    public async Task ParseAsync_MalformedXml_ReturnsMalformedResult_DoesNotThrow()
    {
        var xml = "<movie><title>Broken</title>"; // unclosed root element

        var path = await WriteTempNfoAsync(xml);

        var act = async () => await _sut.ParseAsync(path);
        await act.Should().NotThrowAsync("Parser must be fault-tolerant on malformed XML");

        var result = await _sut.ParseAsync(path);
        result.ParsedSuccessfully.Should().BeFalse();
        result.Warning.Should().NotBeNullOrEmpty();
        result.TmdbId.Should().BeNull();
        result.Title.Should().BeNull();
    }

    [Fact]
    public async Task ParseAsync_EmptyFile_ReturnsMalformed()
    {
        var path = await WriteTempNfoAsync(string.Empty);

        var result = await _sut.ParseAsync(path);

        result.ParsedSuccessfully.Should().BeFalse();
        result.Warning.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ParseAsync_NonXmlContent_ReturnsMalformed_DoesNotThrow()
    {
        var path = await WriteTempNfoAsync("This is not XML at all!!! ###");

        var act = async () => await _sut.ParseAsync(path);
        await act.Should().NotThrowAsync();

        var result = await _sut.ParseAsync(path);
        result.ParsedSuccessfully.Should().BeFalse();
    }

    [Fact]
    public async Task ParseAsync_NonExistentFile_ReturnsMalformed_DoesNotThrow()
    {
        var nonExistentPath = "/tmp/this-file-does-not-exist-xyz-12345.nfo";

        var act = async () => await _sut.ParseAsync(nonExistentPath);
        await act.Should().NotThrowAsync("Parser must not throw even for missing files");

        var result = await _sut.ParseAsync(nonExistentPath);
        result.ParsedSuccessfully.Should().BeFalse();
        result.Warning.Should().NotBeNullOrEmpty();
    }

    // =========================================================================
    // Missing optional fields — null without failing
    // SOURCE: tasks.md spec — missing optional fields return null without failing
    // =========================================================================

    [Fact]
    public async Task ParseAsync_NfoWithNoTmdbId_ReturnsSuccessWithNullTmdbId()
    {
        var xml = """
            <movie>
                <title>Parasite</title>
                <year>2019</year>
            </movie>
            """;

        var path = await WriteTempNfoAsync(xml);
        var result = await _sut.ParseAsync(path);

        result.ParsedSuccessfully.Should().BeTrue();
        result.Title.Should().Be("Parasite");
        result.Year.Should().Be(2019);
        result.TmdbId.Should().BeNull("TmdbId should be null when <tmdbid> element is absent");
    }

    [Fact]
    public async Task ParseAsync_NfoWithOnlyTitle_ReturnsSuccessWithNullYearAndNullTmdbId()
    {
        var xml = """
            <movie>
                <title>Minimal Movie</title>
            </movie>
            """;

        var path = await WriteTempNfoAsync(xml);
        var result = await _sut.ParseAsync(path);

        result.ParsedSuccessfully.Should().BeTrue();
        result.Title.Should().Be("Minimal Movie");
        result.Year.Should().BeNull();
        result.TmdbId.Should().BeNull();
    }

    [Fact]
    public async Task ParseAsync_NfoWithNoTitle_ReturnsSuccessWithNullTitle()
    {
        var xml = """
            <movie>
                <tmdbid>12345</tmdbid>
                <year>2020</year>
            </movie>
            """;

        var path = await WriteTempNfoAsync(xml);
        var result = await _sut.ParseAsync(path);

        result.ParsedSuccessfully.Should().BeTrue();
        result.Title.Should().BeNull();
        result.TmdbId.Should().Be(12345);
        result.Year.Should().Be(2020);
    }

    // =========================================================================
    // tvshow.nfo shape
    // SOURCE: Kodi wiki — https://kodi.wiki/view/NFO_files/TV_shows
    // =========================================================================

    [Fact]
    public async Task ParseAsync_TvShowNfo_ParsesTitleYearAndTmdbId()
    {
        var xml = """
            <tvshow>
                <title>Breaking Bad</title>
                <year>2008</year>
                <tmdbid>1396</tmdbid>
                <imdbid>tt0903747</imdbid>
            </tvshow>
            """;

        var path = await WriteTempNfoAsync(xml);
        var result = await _sut.ParseAsync(path);

        result.ParsedSuccessfully.Should().BeTrue();
        result.Title.Should().Be("Breaking Bad");
        result.Year.Should().Be(2008);
        result.TmdbId.Should().Be(1396);
        result.ImdbId.Should().Be("tt0903747");
        // TV show NFOs have no season/episode at the show level
        result.Season.Should().BeNull();
        result.Episode.Should().BeNull();
    }

    [Fact]
    public async Task ParseAsync_TvShowNfo_FailsGracefully_WhenTmdbIdAbsent()
    {
        var xml = """
            <tvshow>
                <title>Some Show</title>
                <year>2015</year>
            </tvshow>
            """;

        var path = await WriteTempNfoAsync(xml);
        var result = await _sut.ParseAsync(path);

        result.ParsedSuccessfully.Should().BeTrue();
        result.TmdbId.Should().BeNull();
        result.Title.Should().Be("Some Show");
    }

    // =========================================================================
    // Per-episode .nfo shape
    // SOURCE: Kodi wiki — https://kodi.wiki/view/NFO_files/TV_shows#Episode_NFOs
    // =========================================================================

    [Fact]
    public async Task ParseAsync_EpisodeNfo_ParseesSeasonAndEpisodeNumbers()
    {
        var xml = """
            <episodedetails>
                <title>Pilot</title>
                <season>1</season>
                <episode>1</episode>
                <tmdbid>7200</tmdbid>
            </episodedetails>
            """;

        var path = await WriteTempNfoAsync(xml);
        var result = await _sut.ParseAsync(path);

        result.ParsedSuccessfully.Should().BeTrue();
        result.Title.Should().Be("Pilot");
        result.Season.Should().Be(1);
        result.Episode.Should().Be(1);
        result.TmdbId.Should().Be(7200);
    }

    [Fact]
    public async Task ParseAsync_EpisodeNfo_MissingSeasonAndEpisode_ReturnsNulls()
    {
        var xml = """
            <episodedetails>
                <title>Some Episode Without Numbers</title>
            </episodedetails>
            """;

        var path = await WriteTempNfoAsync(xml);
        var result = await _sut.ParseAsync(path);

        result.ParsedSuccessfully.Should().BeTrue();
        result.Season.Should().BeNull();
        result.Episode.Should().BeNull();
    }

    // =========================================================================
    // IMDB id extraction — from <id> element (Kodi legacy) or <imdbid>
    // SOURCE: Kodi wiki — older NFO files use <id> for the IMDB id
    // =========================================================================

    [Fact]
    public async Task ParseAsync_NfoWithLegacyIdElement_ExtractsImdbId()
    {
        var xml = """
            <movie>
                <title>Se7en</title>
                <year>1995</year>
                <id>tt0114369</id>
            </movie>
            """;

        var path = await WriteTempNfoAsync(xml);
        var result = await _sut.ParseAsync(path);

        result.ParsedSuccessfully.Should().BeTrue();
        result.ImdbId.Should().Be("tt0114369");
    }

    // =========================================================================
    // Whitespace tolerance — values with surrounding whitespace are trimmed
    // =========================================================================

    [Fact]
    public async Task ParseAsync_NfoWithWhitespacePaddedValues_TrimsValues()
    {
        var xml = """
            <movie>
                <title>  Inception  </title>
                <year>  2010  </year>
                <tmdbid>  27205  </tmdbid>
            </movie>
            """;

        var path = await WriteTempNfoAsync(xml);
        var result = await _sut.ParseAsync(path);

        result.ParsedSuccessfully.Should().BeTrue();
        result.Title.Should().Be("Inception");
        result.Year.Should().Be(2010);
        result.TmdbId.Should().Be(27205);
    }
}



