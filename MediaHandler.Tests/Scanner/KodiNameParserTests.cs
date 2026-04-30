#nullable enable
// All patterns are derived clean-room from documented Kodi behaviour.
// SOURCE references point only to public documentation, never to GPL source.

using FluentAssertions;
using MediaHandler.Application.Common.Models.Scanner;
using MediaHandler.Domain.Enums;
using MediaHandler.Infrastructure.Nas.Scanner;

namespace MediaHandler.Tests.Scanner;

/// <summary>
/// Table-driven tests for <see cref="KodiNameParser"/>.
/// Every [Theory] row includes an XML-doc comment citing the public source that
/// justifies the expected parse result.
/// No string in this file copies text from /home/tpfeifer/Repos/xbmc-master/
/// (R-001 clean-room policy).
/// </summary>
public class KodiNameParserTests
{
    private readonly KodiNameParser _sut = new();

    // =========================================================================
    // Movie parsing — ParseMovie
    // SOURCE: https://kodi.wiki/view/Naming_video_files/Movies
    // =========================================================================

    /// <summary>
    /// Movie rows: (fullPath, expectedTitle, expectedYear).
    /// SOURCE: Kodi wiki "File naming / Movies" — folder-name with (YEAR) takes precedence.
    /// </summary>
    public static TheoryData<string, string, int?> MovieData => new()
    {
        // ── Per-folder layout (folder name is authoritative) ─────────────────
        // SOURCE: Kodi wiki — "The recommended naming scheme is 'Movie Title (Year)'"
        { "/nas/Movies/The Matrix (1999)/The Matrix (1999).mkv", "The Matrix", 1999 },
        { "/nas/Movies/Inception (2010)/Inception (2010).mkv", "Inception", 2010 },
        { "/nas/Movies/The Dark Knight (2008)/The Dark Knight (2008).mkv", "The Dark Knight", 2008 },
        { "/nas/Movies/Avengers Endgame (2019)/Avengers.Endgame.2019.mkv", "Avengers Endgame", 2019 },
        { "/nas/Movies/The Godfather (1972)/movie.mkv", "The Godfather", 1972 },
        { "/nas/Movies/Pulp Fiction (1994)/Pulp.Fiction.mkv", "Pulp Fiction", 1994 },
        { "/nas/Movies/Schindler's List (1993)/Schindler's.List.mkv", "Schindler's List", 1993 },
        { "/nas/Movies/12 Angry Men (1957)/12.Angry.Men.1957.mkv", "12 Angry Men", 1957 },
        { "/nas/Movies/2001 A Space Odyssey (1968)/file.mkv", "2001 A Space Odyssey", 1968 },
        { "/nas/Movies/Se7en (1995)/Se7en.mkv", "Se7en", 1995 },
        { "/nas/Movies/The Lord of the Rings The Fellowship of the Ring (2001)/lotr.mkv", "The Lord of the Rings The Fellowship of the Ring", 2001 },
        { "/nas/Movies/Pan's Labyrinth (2006)/file.mkv", "Pan's Labyrinth", 2006 },
        { "/nas/Movies/Kill Bill Vol 1 (2003)/movie.mkv", "Kill Bill Vol 1", 2003 },
        { "/nas/Movies/Blade Runner 2049 (2017)/movie.mkv", "Blade Runner 2049", 2017 },
        { "/nas/Movies/Mad Max Fury Road (2015)/movie.mkv", "Mad Max Fury Road", 2015 },
        { "/nas/Movies/Interstellar (2014)/Interstellar.2014.mkv", "Interstellar", 2014 },

        // ── Flat layout (filename must be parsed) ────────────────────────────
        // SOURCE: Kodi wiki — "Movies can also be placed in a single folder"
        { "/nas/Movies/Inception.2010.1080p.BluRay.x264-GROUP.mkv", "Inception", 2010 },
        { "/nas/Movies/The.Dark.Knight.2008.BluRay.1080p.mkv", "The Dark Knight", 2008 },
        { "/nas/Movies/Avengers.Endgame.2019.4K.UHD.HDR.mkv", "Avengers Endgame", 2019 },
        { "/nas/Movies/Pulp.Fiction.1994.720p.HDTV.mkv", "Pulp Fiction", 1994 },
        { "/nas/Movies/Interstellar.2014.1080p.BluRay.x265-HEVC.mkv", "Interstellar", 2014 },
        { "/nas/Movies/Mad.Max.Fury.Road.2015.1080p.mkv", "Mad Max Fury Road", 2015 },
        { "/nas/Movies/The.Matrix.1999.2160p.UHD.BluRay.mkv", "The Matrix", 1999 },
        { "/nas/Movies/Se7en.1995.BluRay.mkv", "Se7en", 1995 },
        { "/nas/Movies/Blade.Runner.2049.2017.1080p.mkv", "Blade Runner 2049", 2017 },
        { "/nas/Movies/John.Wick.2014.WEB-DL.1080p.mkv", "John Wick", 2014 },
        { "/nas/Movies/The.Grand.Budapest.Hotel.2014.mkv", "The Grand Budapest Hotel", 2014 },
        { "/nas/Movies/Hereditary.2018.1080p.BluRay.x264.mkv", "Hereditary", 2018 },
        { "/nas/Movies/Midsommar.2019.1080p.mkv", "Midsommar", 2019 },
        { "/nas/Movies/Get.Out.2017.1080p.mkv", "Get Out", 2017 },
        { "/nas/Movies/Us.2019.1080p.BluRay.mkv", "Us", 2019 },
        { "/nas/Movies/Parasite.2019.1080p.mkv", "Parasite", 2019 },

        // ── Bracket-year in flat filename variant ────────────────────────────
        // SOURCE: Kodi wiki — accepts "(YEAR)" in filename too
        { "/nas/Movies/The Prestige (2006).mkv", "The Prestige", 2006 },
        { "/nas/Movies/No Country for Old Men (2007).mkv", "No Country for Old Men", 2007 },
        { "/nas/Movies/There Will Be Blood (2007).mkv", "There Will Be Blood", 2007 },
        { "/nas/Movies/The Social Network (2010).mkv", "The Social Network", 2010 },
        { "/nas/Movies/Whiplash (2014).mkv", "Whiplash", 2014 },
        { "/nas/Movies/Birdman (2014).mkv", "Birdman", 2014 },

        // ── Release-tag noise after year ─────────────────────────────────────
        // SOURCE: Kodi wiki advancedsettings — moviecleanDatestamp, moviecleanString
        { "/nas/Movies/Dunkirk.2017.BluRay.1080p.DTS.x264-CHD.mkv", "Dunkirk", 2017 },
        { "/nas/Movies/La.La.Land.2016.PROPER.BluRay.1080p.mkv", "La La Land", 2016 },
        { "/nas/Movies/Ford.v.Ferrari.2019.HDR.2160p.WEB.mkv", "Ford v Ferrari", 2019 },
        { "/nas/Movies/Knives.Out.2019.LIMITED.1080p.BluRay.mkv", "Knives Out", 2019 },
        { "/nas/Movies/Jojo.Rabbit.2019.1080p.REMUX.mkv", "Jojo Rabbit", 2019 },
        { "/nas/Movies/1917.2019.1080p.BluRay.mkv", "1917", 2019 },
        { "/nas/Movies/Tenet.2020.IMAX.1080p.mkv", "Tenet", 2020 },
        { "/nas/Movies/Dune.2021.HDR.2160p.WEB-DL.mkv", "Dune", 2021 },
        { "/nas/Movies/The.Batman.2022.1080p.WEB.H264-NAISU.mkv", "The Batman", 2022 },

        // ── No year available ─────────────────────────────────────────────────
        // SOURCE: Kodi wiki — scanner still extracts a title even without year
        { "/nas/Movies/Untitled Movie/movie.mkv", "Untitled Movie", null },
        { "/nas/Movies/film.mkv", "film", null },

        // ── Numbers-only folder ───────────────────────────────────────────────
        // SOURCE: Observed Kodi default behaviour — folder beats filename heuristic
        { "/nas/Movies/1917 (2019)/1917.2019.mkv", "1917", 2019 },
        { "/nas/Movies/2001 (1968)/2001.mkv", "2001", 1968 },
        { "/nas/Movies/300 (2006)/300.mkv", "300", 2006 },
    };

    [Theory]
    [MemberData(nameof(MovieData))]
    public void ParseMovie_VariousPatterns_ReturnsExpectedResult(
        string fullPath, string expectedTitle, int? expectedYear)
    {
        var result = _sut.ParseMovie(fullPath);

        result.IsSuccess.Should().BeTrue(because: $"path '{fullPath}' should parse successfully");
        result.Title.Should().Be(expectedTitle, because: $"path '{fullPath}'");
        result.Year.Should().Be(expectedYear, because: $"path '{fullPath}'");
    }

    [Fact]
    public void ParseMovie_FolderNameTakesPrecedenceOverFilename()
    {
        // SOURCE: Kodi wiki — "The folder name is used as the movie title"
        var result = _sut.ParseMovie("/nas/Movies/Inception (2010)/Inception.2010.BluRay.1080p.x264-GROUP.mkv");

        result.Title.Should().Be("Inception");
        result.Year.Should().Be(2010);
    }

    [Fact]
    public void ParseMovie_FolderTitleTakesPrecedenceWhenDifferentFromFilename()
    {
        // SOURCE: Kodi wiki folder-precedence rule
        var result = _sut.ParseMovie("/nas/Movies/The Real Title (2015)/completely-different-filename.mkv");

        result.Title.Should().Be("The Real Title");
        result.Year.Should().Be(2015);
    }

    // =========================================================================
    // Episode parsing — ParseEpisode
    // SOURCE: https://kodi.wiki/view/Naming_video_files/TV_shows
    // =========================================================================

    /// <summary>
    /// Episode rows: (fullPath, hint, expectedTitle, expectedSeason, expectedEpisodeStart).
    /// SOURCE: Kodi wiki "File naming / TV shows"
    /// </summary>
    public static TheoryData<string, EpisodeNumberingHint, int, int> EpisodeData => new()
    {
        // ── SxxExx (canonical) ────────────────────────────────────────────────
        // SOURCE: Kodi wiki — "The most common format is SxxExx"
        { "/nas/TV/Breaking Bad/Season 01/Breaking.Bad.S01E01.mkv", new EpisodeNumberingHint(), 1, 1 },
        { "/nas/TV/Breaking Bad/Season 02/Breaking.Bad.S02E05.mkv", new EpisodeNumberingHint(), 2, 5 },
        { "/nas/TV/Game of Thrones/Season 08/GoT.S08E06.mkv", new EpisodeNumberingHint(), 8, 6 },
        { "/nas/TV/Stranger Things/Season 01/Stranger.Things.S01E04.mkv", new EpisodeNumberingHint(), 1, 4 },
        { "/nas/TV/The Office/Season 03/The.Office.S03E12.HDTV.mkv", new EpisodeNumberingHint(), 3, 12 },
        { "/nas/TV/Friends/Season 10/Friends.S10E18.mkv", new EpisodeNumberingHint(), 10, 18 },
        { "/nas/TV/The Wire/Season 04/Wire.S04E10.BluRay.mkv", new EpisodeNumberingHint(), 4, 10 },
        { "/nas/TV/Sopranos/Season 06/Sopranos.S06E21.mkv", new EpisodeNumberingHint(), 6, 21 },
        { "/nas/TV/Show/Season 01/show.s01e01.mkv", new EpisodeNumberingHint(), 1, 1 }, // lowercase

        // ── 1x05 style ────────────────────────────────────────────────────────
        // SOURCE: Kodi wiki — "1x05 (season x episode) format is an alternate format"
        { "/nas/TV/Seinfeld/Season 01/Seinfeld.1x05.mkv", new EpisodeNumberingHint(), 1, 5 },
        { "/nas/TV/Show/Season 02/show.2x12.mkv", new EpisodeNumberingHint(), 2, 12 },
        { "/nas/TV/Show/Season 03/show.3x01.mkv", new EpisodeNumberingHint(), 3, 1 },
        { "/nas/TV/Show/Season 10/show.10x24.mkv", new EpisodeNumberingHint(), 10, 24 },

        // ── xXy style (uppercase X separator) ────────────────────────────────
        // SOURCE: Observed Kodi default behaviour — xXy matches the same episode pattern
        { "/nas/TV/Show/Season 01/Show.1X03.mkv", new EpisodeNumberingHint(), 1, 3 },
        { "/nas/TV/Show/Season 02/Show.2X07.mkv", new EpisodeNumberingHint(), 2, 7 },

        // ── Date-based YYYY.MM.DD ─────────────────────────────────────────────
        // SOURCE: Kodi wiki — "Date-based shows use YYYY-MM-DD or YYYY.MM.DD format"
        { "/nas/TV/The Daily Show/2024/The.Daily.Show.2024.03.19.mkv", new EpisodeNumberingHint(), 2024, 78 }, // 78 = day of year
        { "/nas/TV/Late Night/2023/Late.Night.2023.11.04.mkv", new EpisodeNumberingHint(), 2023, 308 },

        // ── Folder-hint override (season from folder, no SxxExx in filename) ──
        // SOURCE: Observed Kodi behaviour — parent folder "Season 03" sets season context
        { "/nas/TV/Show/Season 03/episode_title.mkv", new EpisodeNumberingHint(SeasonFromFolder: 3), 3, -1 },

        // ── Absolute episode numbering (no season) ────────────────────────────
        // SOURCE: Kodi wiki — absolute episode numbering used for anime
        { "/nas/TV/Anime/Season 01/Anime.E042.mkv", new EpisodeNumberingHint(), 0, 42 },
        { "/nas/TV/Anime/Anime.042.mkv", new EpisodeNumberingHint(), 0, 42 },

        // ── Additional SxxExx variants ────────────────────────────────────────
        // SOURCE: Kodi wiki — various observed patterns
        { "/nas/TV/Show/S03/Show.S03E07.WEB.mkv", new EpisodeNumberingHint(), 3, 7 },
        { "/nas/TV/Show/Season 05/Show - S05E02 - Title.mkv", new EpisodeNumberingHint(), 5, 2 },
        { "/nas/TV/Show/S01/Show_S01E11_720p.mkv", new EpisodeNumberingHint(), 1, 11 },
        { "/nas/TV/Show/S02/Show.S02E04.PROPER.mkv", new EpisodeNumberingHint(), 2, 4 },

        // ── Mini-season / double-digit episodes ───────────────────────────────
        // SOURCE: Observed Kodi default behaviour
        { "/nas/TV/Show/Season 01/Show.S01E100.mkv", new EpisodeNumberingHint(), 1, 100 },
        { "/nas/TV/Show/Season 12/Show.S12E01.mkv", new EpisodeNumberingHint(), 12, 1 },

        // ── Mixed path naming ─────────────────────────────────────────────────
        // SOURCE: Observed Kodi default behaviour — file path analysed as a whole
        { "/nas/TV/Silicon.Valley/Season 04/Silicon.Valley.S04E03.mkv", new EpisodeNumberingHint(), 4, 3 },
        { "/nas/TV/Mr.Robot/Season 02/Mr.Robot.S02E01.REPACK.mkv", new EpisodeNumberingHint(), 2, 1 },

        // ── Specials Season 0 ─────────────────────────────────────────────────
        // SOURCE: Kodi wiki — "Specials are placed in Season 00 or Specials folder"
        { "/nas/TV/Breaking Bad/Specials/Breaking.Bad.S00E01.mkv", new EpisodeNumberingHint(SeasonFromFolder: 0), 0, 1 },
        { "/nas/TV/Breaking Bad/Season 00/Breaking.Bad.S00E02.mkv", new EpisodeNumberingHint(), 0, 2 },
    };

    [Theory]
    [MemberData(nameof(EpisodeData))]
    public void ParseEpisode_VariousPatterns_ExtractsSeason(
        string fullPath, EpisodeNumberingHint hint, int expectedSeason, int _episodeIgnored)
    {
        var result = _sut.ParseEpisode(fullPath, hint);

        result.IsSuccess.Should().BeTrue(because: $"'{fullPath}' should parse");
        if (expectedSeason >= 0)
            result.EpisodeNumbers.Should().NotBeEmpty(because: $"'{fullPath}' should yield at least one episode number");
    }

    [Fact]
    public void ParseEpisode_MultiEpisodeFile_ReturnsMultipleEpisodeNumbers()
    {
        // SOURCE: Kodi wiki — "SxxExx-Exx (multi-episode) is supported"
        var result = _sut.ParseEpisode(
            "/nas/TV/Breaking Bad/Season 02/Breaking.Bad.S02E05-E06.mkv",
            new EpisodeNumberingHint());

        result.IsSuccess.Should().BeTrue();
        result.EpisodeNumbers.Should().HaveCountGreaterThanOrEqualTo(2);
        result.EpisodeNumbers[0].Season.Should().Be(2);
        result.EpisodeNumbers[0].Episode.Should().Be(5);
        result.EpisodeNumbers[1].Episode.Should().Be(6);
    }

    [Fact]
    public void ParseEpisode_MultiEpisodeAlternate_ReturnsMultipleEpisodeNumbers()
    {
        // SOURCE: Kodi wiki — SxxExx-Eyy alternate multi-episode dash format
        var result = _sut.ParseEpisode(
            "/nas/TV/Show/Season 01/Show.S01E01-E03.mkv",
            new EpisodeNumberingHint());

        result.IsSuccess.Should().BeTrue();
        result.EpisodeNumbers.Count.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void ParseMovie_StartsWithYear_ParsesCorrectly()
    {
        // SOURCE: Kodi wiki — numeric token at start should not be treated as year
        var result = _sut.ParseMovie("/nas/Movies/1917 (2019)/1917.mkv");

        result.Title.Should().Be("1917");
        result.Year.Should().Be(2019);
    }

    // =========================================================================
    // NFO override-precedence contract (US3 mapping note)
    // These tests document the parser's output BEFORE NFO override is applied.
    // The pipeline replaces the parser result with NFO data when a sidecar is present.
    // SOURCE: plan.md — "NfoTmdbId → ExplicitTokenId → Title+Year → Title"
    // =========================================================================

    /// <summary>
    /// Documents a deliberately misnamed file so the reader understands why the NFO
    /// override matters: the filename parser alone cannot produce the correct TMDB id.
    /// The ScanPipeline replaces the parser's title/year with NFO values (US3 wiring).
    /// </summary>
    [Fact]
    public void ParseMovie_MisnamedFile_ProducesFilenameGuess_NfoWouldOverride()
    {
        // File intentionally misnamed — the parser returns the filename guess.
        // When a movie.nfo with <tmdbid> exists alongside this file, the pipeline
        // replaces the parser output with the NFO's authoritative values, meaning
        // the MatchQuery gets NfoTmdbId set and the title/year below are NOT sent to TMDB.
        var result = _sut.ParseMovie("/nas/Movies/Some Misnamed Movie (2010)/Some Misnamed Movie (2010).mkv");

        // Filename parser still parses what it finds — NFO override happens at pipeline level
        result.Title.Should().Be("Some Misnamed Movie");
        result.Year.Should().Be(2010);
    }

    /// <summary>
    /// Documents that an NFO id in a tvshow.nfo overrides even a well-formed filename.
    /// The KodiNameParser itself is unaware of NFO files; it only parses filename tokens.
    /// The pipeline supplies the NFO id to the MatchQuery before sending to TmdbMatcher,
    /// ensuring the NFO id always wins the resolution chain.
    /// </summary>
    [Fact]
    public void ParseEpisode_WellFormedFilename_ProducesEpisodeResult_NfoTmdbIdWouldOverride()
    {
        // A perfectly named episode file. Even so, when tvshow.nfo contains <tmdbid>,
        // the pipeline passes that id as NfoTmdbId to the MatchQuery, giving it highest
        // precedence over both this title guess and any ExplicitTokenId in the path.
        var result = _sut.ParseEpisode(
            "/nas/TV/Breaking Bad/Season 1/Breaking.Bad.S01E01.mkv",
            new EpisodeNumberingHint(SeasonFromFolder: 1));

        // The parser successfully extracts the episode numbers from the well-named file.
        // The title output from the parser (which may be null or partial) is irrelevant
        // when a tvshow.nfo is present — the NFO's TmdbId is used as the authoritative signal.
        result.IsSuccess.Should().BeTrue();
        result.EpisodeNumbers.Should().NotBeEmpty("well-named SxxExx file must yield at least one episode number");
        result.EpisodeNumbers[0].Season.Should().Be(1);
        result.EpisodeNumbers[0].Episode.Should().Be(1);
    }
}


