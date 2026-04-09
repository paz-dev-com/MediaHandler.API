using FluentAssertions;
using MediaHandler.Infrastructure.Nas;

namespace MediaHandler.Tests.Features.Files;

public class MediaFileNameParserTests
{
    private readonly MediaFileNameParser _parser = new();

    // ── Standard movie filename ───────────────────────────────────────────

    [Fact]
    public void Parse_MovieWithYearAndQuality_ExtractsTitleAndYear()
    {
        var result = _parser.Parse("The.Matrix.1999.1080p.BluRay.mkv");

        result.Should().NotBeNull();
        result!.Title.Should().Be("The Matrix");
        result.Year.Should().Be(1999);
        result.MediaTypeHint.Should().BeNull("no path segments to infer type from a flat filename");
    }

    [Fact]
    public void Parse_FilenameWithoutYear_ExtractsTitleOnly()
    {
        var result = _parser.Parse("Inception.mkv");

        result.Should().NotBeNull();
        result!.Title.Should().Be("Inception");
        result.Year.Should().BeNull();
        result.MediaTypeHint.Should().BeNull();
    }

    // ── Media type hint from path segments ───────────────────────────────

    [Fact]
    public void Parse_PathWithMoviesSegment_SetsMovieHint()
    {
        // Flat structure: /Movies/<filename>
        // The parent folder "Movies" will be used as title candidate, but the hint is key
        var result = _parser.Parse("/nas/Movies/The.Matrix.1999.mkv");

        result.Should().NotBeNull();
        result!.MediaTypeHint.Should().Be("movie");
    }

    [Fact]
    public void Parse_PathWithSeriesSegment_SetsTvHint()
    {
        // Two-level structure: /Series/<ShowFolder>/<episode>
        var result = _parser.Parse("/nas/Series/Breaking Bad/Breaking.Bad.S01E01.720p.mkv");

        result.Should().NotBeNull();
        result!.MediaTypeHint.Should().Be("tv");
        result.Title.Should().Be("Breaking Bad");
    }

    [Fact]
    public void Parse_PathWithTvShowsSegment_SetsTvHint()
    {
        var result = _parser.Parse("/mnt/TV Shows/The.Wire.S01E01.mkv");

        result.Should().NotBeNull();
        result!.MediaTypeHint.Should().Be("tv");
    }

    // ── TV episode pattern in filename ───────────────────────────────────

    [Fact]
    public void Parse_TvEpisodePatternInFilename_SetsTvHintAndExtractsTitle()
    {
        var result = _parser.Parse("breaking.bad.s01e01.720p.mkv");

        result.Should().NotBeNull();
        result!.MediaTypeHint.Should().Be("tv");
        result.Title.Should().Be("Breaking Bad");
        result.Year.Should().BeNull();
    }

    [Fact]
    public void Parse_TvEpisodeInDeepPath_TitleFromParentFolder()
    {
        // Parent folder "The Wire" provides the title
        var result = _parser.Parse("/Series/The Wire/Season 01/The.Wire.S01E01.mkv");

        result.Should().NotBeNull();
        result!.MediaTypeHint.Should().Be("tv");
        result.Title.Should().Be("The Wire");
    }

    // ── Year in parent folder ─────────────────────────────────────────────

    [Fact]
    public void Parse_MovieInNamedFolder_ExtractsTitleFromFolder()
    {
        // "The Matrix 1999" folder → title + year from folder name
        var result = _parser.Parse("/Movies/The Matrix 1999/The.Matrix.mkv");

        result.Should().NotBeNull();
        result!.Title.Should().Be("The Matrix");
        result.Year.Should().Be(1999);
        result.MediaTypeHint.Should().Be("movie");
    }

    // ── Quality / group tag stripping ─────────────────────────────────────

    [Fact]
    public void Parse_FilenameWithGroupTag_StripsTagFromTitle()
    {
        // Flat filename with bracket-style group tag — no parent folder context
        var result = _parser.Parse("Inception.2010.1080p.BluRay.[YTS].mkv");

        result.Should().NotBeNull();
        result!.Title.Should().Be("Inception");
        result.Year.Should().Be(2010);
    }

    // ── Edge cases ────────────────────────────────────────────────────────

    [Fact]
    public void Parse_EmptyPath_ReturnsNull()
    {
        _parser.Parse(string.Empty).Should().BeNull();
        _parser.Parse("   ").Should().BeNull();
    }

    [Fact]
    public void Parse_NonVideoExtension_ReturnsNull()
    {
        _parser.Parse("/Movies/document.pdf").Should().BeNull();
        _parser.Parse("/Movies/archive.zip").Should().BeNull();
    }

    [Fact]
    public void Parse_UnderscoresSeparated_TreatedAsSpaces()
    {
        var result = _parser.Parse("The_Dark_Knight_2008_1080p.mkv");

        result.Should().NotBeNull();
        result!.Title.Should().Be("The Dark Knight");
        result.Year.Should().Be(2008);
    }
}


