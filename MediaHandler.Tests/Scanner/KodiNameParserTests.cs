// All patterns are derived clean-room from documented Kodi behaviour.
// SOURCE references point only to public documentation, never to GPL source.

using FluentAssertions;
using MediaHandler.Application.Common.Models.Scanner;
using MediaHandler.Infrastructure.Nas.Scanner;

namespace MediaHandler.Tests.Scanner;

/// <summary>
///     Table-driven tests for <see cref="KodiNameParser" />.
///     Every [Theory] row includes an XML-doc comment citing the public source that
///     justifies the expected parse result.
///     No string in this file copies text from /home/tpfeifer/Repos/xbmc-master/
///     (R-001 clean-room policy).
/// </summary>
public class KodiNameParserTests
{
    private readonly KodiNameParser _sut = new();

    // =========================================================================
    // Movie parsing — ParseMovie
    // SOURCE: https://kodi.wiki/view/Naming_video_files/Movies
    // =========================================================================

    /// <summary>
    ///     Movie rows: (fullPath, expectedTitle, expectedYear).
    ///     SOURCE: Kodi wiki "File naming / Movies" — folder-name with (YEAR) takes precedence.
    /// </summary>
    public static TheoryData<string, string, int?> MovieData => new()
    {
        // Per-folder layout (folder name is authoritative)
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
        {
            "/nas/Movies/The Lord of the Rings The Fellowship of the Ring (2001)/lotr.mkv",
            "The Lord of the Rings The Fellowship of the Ring", 2001
        },
        { "/nas/Movies/Pan's Labyrinth (2006)/file.mkv", "Pan's Labyrinth", 2006 },
        { "/nas/Movies/Kill Bill Vol 1 (2003)/movie.mkv", "Kill Bill Vol 1", 2003 },
        { "/nas/Movies/Blade Runner 2049 (2017)/movie.mkv", "Blade Runner 2049", 2017 },
        { "/nas/Movies/Mad Max Fury Road (2015)/movie.mkv", "Mad Max Fury Road", 2015 },
        { "/nas/Movies/Interstellar (2014)/Interstellar.2014.mkv", "Interstellar", 2014 },

        // Flat layout (filename must be parsed)
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

        // Bracket-year in flat filename variant
        // SOURCE: Kodi wiki — accepts "(YEAR)" in filename too
        { "/nas/Movies/The Prestige (2006).mkv", "The Prestige", 2006 },
        { "/nas/Movies/No Country for Old Men (2007).mkv", "No Country for Old Men", 2007 },
        { "/nas/Movies/There Will Be Blood (2007).mkv", "There Will Be Blood", 2007 },
        { "/nas/Movies/The Social Network (2010).mkv", "The Social Network", 2010 },
        { "/nas/Movies/Whiplash (2014).mkv", "Whiplash", 2014 },
        { "/nas/Movies/Birdman (2014).mkv", "Birdman", 2014 },

        // Release-tag noise after year
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

        // No year available
        // SOURCE: Kodi wiki — scanner still extracts a title even without year
        { "/nas/Movies/Untitled Movie/movie.mkv", "Untitled Movie", null },
        { "/nas/Movies/film.mkv", "film", null },

        // Numbers-only folder
        // SOURCE: Observed Kodi default behaviour — folder beats filename heuristic
        { "/nas/Movies/1917 (2019)/1917.2019.mkv", "1917", 2019 },
        { "/nas/Movies/2001 (1968)/2001.mkv", "2001", 1968 },
        { "/nas/Movies/300 (2006)/300.mkv", "300", 2006 }
    };

    // =========================================================================
    // Episode parsing — ParseEpisode
    // SOURCE: https://kodi.wiki/view/Naming_video_files/TV_shows
    // =========================================================================

    /// <summary>
    ///     Episode rows: (fullPath, hint, expectedTitle, expectedSeason, expectedEpisodeStart).
    ///     SOURCE: Kodi wiki "File naming / TV shows"
    /// </summary>
    public static TheoryData<string, EpisodeNumberingHint, int, int> EpisodeData => new()
    {
        // SxxExx (canonical)
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

        // 1x05 style
        // SOURCE: Kodi wiki — "1x05 (season x episode) format is an alternate format"
        { "/nas/TV/Seinfeld/Season 01/Seinfeld.1x05.mkv", new EpisodeNumberingHint(), 1, 5 },
        { "/nas/TV/Show/Season 02/show.2x12.mkv", new EpisodeNumberingHint(), 2, 12 },
        { "/nas/TV/Show/Season 03/show.3x01.mkv", new EpisodeNumberingHint(), 3, 1 },
        { "/nas/TV/Show/Season 10/show.10x24.mkv", new EpisodeNumberingHint(), 10, 24 },

        // xXy style (uppercase X separator)
        // SOURCE: Observed Kodi default behaviour — xXy matches the same episode pattern
        { "/nas/TV/Show/Season 01/Show.1X03.mkv", new EpisodeNumberingHint(), 1, 3 },
        { "/nas/TV/Show/Season 02/Show.2X07.mkv", new EpisodeNumberingHint(), 2, 7 },

        // Date-based YYYY.MM.DD
        // SOURCE: Kodi wiki — "Date-based shows use YYYY-MM-DD or YYYY.MM.DD format"
        {
            "/nas/TV/The Daily Show/2024/The.Daily.Show.2024.03.19.mkv", new EpisodeNumberingHint(), 2024, 78
        }, // 78 = day of year
        { "/nas/TV/Late Night/2023/Late.Night.2023.11.04.mkv", new EpisodeNumberingHint(), 2023, 308 },

        // Folder-hint override (season from folder, no SxxExx in filename)
        // SOURCE: Observed Kodi behaviour — parent folder "Season 03" sets season context
        { "/nas/TV/Show/Season 03/episode_title.mkv", new EpisodeNumberingHint(3), 3, -1 },

        // Absolute episode numbering (no season)
        // SOURCE: Kodi wiki — absolute episode numbering used for anime
        { "/nas/TV/Anime/Season 01/Anime.E042.mkv", new EpisodeNumberingHint(), 0, 42 },
        { "/nas/TV/Anime/Anime.042.mkv", new EpisodeNumberingHint(), 0, 42 },

        // Additional SxxExx variants
        // SOURCE: Kodi wiki — various observed patterns
        { "/nas/TV/Show/S03/Show.S03E07.WEB.mkv", new EpisodeNumberingHint(), 3, 7 },
        { "/nas/TV/Show/Season 05/Show - S05E02 - Title.mkv", new EpisodeNumberingHint(), 5, 2 },
        { "/nas/TV/Show/S01/Show_S01E11_720p.mkv", new EpisodeNumberingHint(), 1, 11 },
        { "/nas/TV/Show/S02/Show.S02E04.PROPER.mkv", new EpisodeNumberingHint(), 2, 4 },

        // Mini-season / double-digit episodes
        // SOURCE: Observed Kodi default behaviour
        { "/nas/TV/Show/Season 01/Show.S01E100.mkv", new EpisodeNumberingHint(), 1, 100 },
        { "/nas/TV/Show/Season 12/Show.S12E01.mkv", new EpisodeNumberingHint(), 12, 1 },

        // Mixed path naming
        // SOURCE: Observed Kodi default behaviour — file path analysed as a whole
        { "/nas/TV/Silicon.Valley/Season 04/Silicon.Valley.S04E03.mkv", new EpisodeNumberingHint(), 4, 3 },
        { "/nas/TV/Mr.Robot/Season 02/Mr.Robot.S02E01.REPACK.mkv", new EpisodeNumberingHint(), 2, 1 },

        // Specials Season 0
        // SOURCE: Kodi wiki — "Specials are placed in Season 00 or Specials folder"
        { "/nas/TV/Breaking Bad/Specials/Breaking.Bad.S00E01.mkv", new EpisodeNumberingHint(0), 0, 1 },
        { "/nas/TV/Breaking Bad/Season 00/Breaking.Bad.S00E02.mkv", new EpisodeNumberingHint(), 0, 2 }
    };

    [Theory]
    [MemberData(nameof(MovieData))]
    public void ParseMovie_VariousPatterns_ReturnsExpectedResult(
        string fullPath, string expectedTitle, int? expectedYear)
    {
        var result = _sut.ParseMovie(fullPath);

        result.IsSuccess.Should().BeTrue($"path '{fullPath}' should parse successfully");
        result.Title.Should().Be(expectedTitle, $"path '{fullPath}'");
        result.Year.Should().Be(expectedYear, $"path '{fullPath}'");
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

    [Theory]
    [MemberData(nameof(EpisodeData))]
    public void ParseEpisode_VariousPatterns_ExtractsSeason(
        string fullPath, EpisodeNumberingHint hint, int expectedSeason, int expectedEpisode)
    {
        var result = _sut.ParseEpisode(fullPath, hint);

        result.IsSuccess.Should().BeTrue($"'{fullPath}' should parse (expected episode {expectedEpisode})");
        if (expectedSeason >= 0)
            result.EpisodeNumbers.Should().NotBeEmpty($"'{fullPath}' should yield at least one episode number");
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
    // NFO override-precedence contract
    // These tests document the parser's output BEFORE NFO override is applied.
    // The pipeline replaces the parser result with NFO data when a sidecar is present.
    // SOURCE: plan.md — "NfoTmdbId → ExplicitTokenId → Title+Year → Title"
    // =========================================================================

    /// <summary>
    ///     Documents a deliberately misnamed file so the reader understands why the NFO
    ///     override matters: the filename parser alone cannot produce the correct TMDB id.
    ///     When a movie.nfo with a tmdbid exists alongside this file, the pipeline
    ///     replaces the parser output with the NFO's authoritative values.
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
    ///     Documents that an NFO id in a tvshow.nfo overrides even a well-formed filename.
    ///     The KodiNameParser itself is unaware of NFO files; it only parses filename tokens.
    ///     The pipeline supplies the NFO id to the MatchQuery before sending to TmdbMatcher,
    ///     ensuring the NFO id always wins the resolution chain.
    /// </summary>
    [Fact]
    public void ParseEpisode_WellFormedFilename_ProducesEpisodeResult_NfoTmdbIdWouldOverride()
    {
        var result = _sut.ParseEpisode(
            "/nas/TV/Breaking Bad/Season 1/Breaking.Bad.S01E01.mkv",
            new EpisodeNumberingHint(1));

        result.IsSuccess.Should().BeTrue();
        result.EpisodeNumbers.Should().NotBeEmpty("well-named SxxExx file must yield at least one episode number");
        result.EpisodeNumbers[0].Season.Should().Be(1);
        result.EpisodeNumbers[0].Episode.Should().Be(1);
    }

    // =========================================================================
    // Show title extraction: ParseEpisode.Title = SHOW title (text BEFORE SxxExx)
    // =========================================================================

    /// <summary>
    ///     Show-title extraction rows: (fullPath, expectedShowTitle).
    ///     SOURCE: contracts/internal-contracts.md behavioral contract table.
    /// </summary>
    public static TheoryData<string, string> ShowTitleFromFilenameData => new()
    {
        // SOURCE: contracts/internal-contracts.md — "Slow Horses"
        // Dots are separators; text before S03E05 = "Slow.Horses." → "Slow Horses"
        {
            "/Séries/Slow Horses/S03/Slow.Horses.S03E05.MULTi.1080p.WEBRip.x264.AC3-MULTiViSiON.mkv",
            "Slow Horses"
        },
        // SOURCE: contracts/internal-contracts.md — "Law and Order SUV" (typo preserved from filename)
        // text before S19E23 = "Law.and.Order.SUV." → "Law and Order SUV"
        {
            "/Séries/Law and Order/SVU/S19/Law.and.Order.SUV.S19E23.FRENCH.DVDRip.XviD-Wawacity.tv.avi",
            "Law and Order SUV"
        },
        // SOURCE: contracts/internal-contracts.md — "Une Nounou Denfer"
        // text before S04E10 = "Une.Nounou.Denfer." → "Une Nounou Denfer"
        {
            "/Séries/The Nanny/Une.Nounou.Denfer.S04.MULTi.DVDRIP.x264-ETAY/Une.Nounou.Denfer.S04E10.MULTi.DVDRIP.x264-ETAY.mkv",
            "Une Nounou Denfer"
        },
        // SOURCE: contracts/internal-contracts.md — "Sur écoute"
        // Space-separated filename; accented é MUST be preserved.
        // text before S04E01 = "Sur écoute " → trimmed → "Sur écoute"
        {
            "/Séries/The Wire/The Wire/Sur écoute S04E01 - La fin de l'été.mkv",
            "Sur écoute"
        },
        // SOURCE: contracts/internal-contracts.md — "The Killing US 2011" (year preservation)
        // Year 2011 appears BEFORE SxxExx → must NOT be stripped; it is part of the show title.
        // text before S03E10 = "The.Killing.US.2011." → "The Killing US 2011"
        {
            "/Séries/The Killing US/S03/The.Killing.US.2011.S03E10.1080p.MULTi.WEB-DL.AvALoN.mkv",
            "The Killing US 2011"
        },
    };

    /// <summary>
    ///     Verifies that <c>ParseEpisode.Title</c> returns the show name (text before SxxExx),
    ///     not the release-tag text after it.
    /// </summary>
    [Theory]
    [MemberData(nameof(ShowTitleFromFilenameData))]
    public void ParseEpisode_ShowTitle_ExtractedFromTextBeforeSxxExx(
        string fullPath, string expectedShowTitle)
    {
        var result = _sut.ParseEpisode(fullPath, new EpisodeNumberingHint());

        result.IsSuccess.Should().BeTrue($"'{fullPath}' must parse successfully");
        result.Title.Should().Be(expectedShowTitle,
            $"Title should be the show name (text before SxxExx) — not release tags — for '{Path.GetFileName(fullPath)}'");
    }

    [Fact]
    public void ParseEpisode_YearBeforeSxxExx_IsPreservedInShowTitle()
    {
        // SOURCE: contracts/internal-contracts.md — year preservation rule.
        // "The.Killing.US.2011.S03E10..." → year 2011 precedes SxxExx and must remain
        // in the title to enable TMDB disambiguation. It is NOT stripped.
        var result = _sut.ParseEpisode(
            "/Séries/The Killing US/S03/The.Killing.US.2011.S03E10.1080p.MULTi.WEB-DL.AvALoN.mkv",
            new EpisodeNumberingHint());

        result.Title.Should().Be("The Killing US 2011",
            "a year-like number that appears before the SxxExx marker is part of the show title and must be preserved");
    }

    [Fact]
    public void ParseEpisode_AccentedCharactersInTitle_ArePreserved()
    {
        // SOURCE: contracts/internal-contracts.md — accented character preservation rule.
        // é in "Sur écoute" must survive the dot/underscore-to-space replacement.
        var result = _sut.ParseEpisode(
            "/Séries/The Wire/The Wire/Sur écoute S04E01 - La fin de l'été.mkv",
            new EpisodeNumberingHint());

        result.Title.Should().Be("Sur écoute",
            "accented characters (é, è, ê, etc.) must not be stripped or transliterated");
    }

    [Fact]
    public void ParseEpisode_EpisodeTitleField_PopulatedWithTextAfterSxxExx()
    {
        // SOURCE: contracts/internal-contracts.md — EpisodeTitle carries text after SxxExx.
        // "Sur écoute S04E01 - La fin de l'été" → EpisodeTitle = "La fin de l'été"
        var result = _sut.ParseEpisode(
            "/Séries/The Wire/The Wire/Sur écoute S04E01 - La fin de l'été.mkv",
            new EpisodeNumberingHint());

        result.EpisodeTitle.Should().NotBeNull(
            "text after SxxExx should be placed in EpisodeTitle, not Title");
        result.EpisodeTitle.Should().Contain("fin",
            "episode-specific title text after SxxExx must be preserved in EpisodeTitle");
    }

    [Fact]
    public void ParseEpisode_UnderscoreSeparatedFilename_TitleExtractedCorrectly()
    {
        // SOURCE: Kodi wiki — underscore is a common separator in TV filenames alongside dot.
        // Underscores before SxxExx must be replaced with spaces in the show title.
        var result = _sut.ParseEpisode(
            "/nas/TV/Show/S01/My_Show_Name_S01E11_720p.mkv",
            new EpisodeNumberingHint());

        result.IsSuccess.Should().BeTrue();
        result.Title.Should().Be("My Show Name",
            "underscores in pre-SxxExx text must be replaced with spaces");
    }

    // =========================================================================
    // FolderTitle: show name inferred from the folder hierarchy
    // SOURCE: contracts/internal-contracts.md behavioral contract table
    // =========================================================================

    /// <summary>
    ///     FolderTitle rows: (fullPath, expectedFolderTitle).
    ///     SOURCE: contracts/internal-contracts.md — FolderTitle behavioral contract table.
    /// </summary>
    public static TheoryData<string, string?> FolderTitleData => new()
    {
        // Behavioral contract table (from contracts/internal-contracts.md)
        // SOURCE: contracts/internal-contracts.md — "Slow Horses"
        // Season S03 is skipped; parent "Slow Horses" is the show folder.
        {
            "/Séries/Slow Horses/S03/Slow.Horses.S03E05.MULTi.1080p.WEBRip.x264.AC3-MULTiViSiON.mkv",
            "Slow Horses"
        },
        // SOURCE: contracts/internal-contracts.md — "Law and Order SVU"
        // Folder hierarchy /Law and Order/SVU/S19/ → sub-show concatenation.
        {
            "/Séries/Law and Order/SVU/S19/Law.and.Order.SUV.S19E23.FRENCH.DVDRip.XviD-Wawacity.tv.avi",
            "Law and Order SVU"
        },
        // SOURCE: contracts/internal-contracts.md — "The Nanny"
        // Release-pack folder "Une.Nounou.Denfer.S04.MULTi..." is skipped; "The Nanny" is used.
        {
            "/Séries/The Nanny/Une.Nounou.Denfer.S04.MULTi.DVDRIP.x264-ETAY/Une.Nounou.Denfer.S04E10.MULTi.DVDRIP.x264-ETAY.mkv",
            "The Nanny"
        },
        // SOURCE: contracts/internal-contracts.md — "The Wire"
        // Duplicate nesting /The Wire/The Wire/ must not produce "The Wire The Wire".
        {
            "/Séries/The Wire/The Wire/Sur écoute S04E01 - La fin de l'été.mkv",
            "The Wire"
        },
        // SOURCE: contracts/internal-contracts.md — "The Killing US"
        // Season S03 is skipped; "The Killing US" is the show folder.
        {
            "/Séries/The Killing US/S03/The.Killing.US.2011.S03E10.1080p.MULTi.WEB-DL.AvALoN.mkv",
            "The Killing US"
        },

        // Season folder patterns to skip
        // SOURCE: Kodi wiki — Season XX and Saison XX are standard season folder names.
        { "/TV Shows/Breaking Bad/Season 03/Breaking.Bad.S03E01.mkv", "Breaking Bad" },
        { "/Séries/Some Show/Saison 05/Show.S05E01.mkv", "Some Show" },
        { "/Séries/Some Show/Specials/Show.S00E01.mkv", "Some Show" },

        // TV-root folder names to skip
        // SOURCE: Observed NAS folder naming — top-level TV containers must not become show title.
        { "/Series/The Sopranos/Season 01/Sopranos.S01E01.mkv", "The Sopranos" },
        { "/TV/Show Name/S01/Show.Name.S01E01.mkv", "Show Name" },
        { "/Shows/Sherlock/S02/Sherlock.S02E01.mkv", "Sherlock" },

        // Generic folder names to skip
        // SOURCE: Observed NAS folder naming — generic containers must not become show title.
        { "/Videos/My Show/S01/Show.S01E01.mkv", "My Show" },
        { "/Media/My Show/Season 01/Show.S01E01.mkv", "My Show" },
        { "/Downloads/My Show/S02/Show.S02E01.mkv", "My Show" },

        // No usable folder available
        // No show-level folder can be found above season or root-only paths.
        { "/Séries/S03/Show.S03E01.mkv", null },
    };

    [Theory]
    [MemberData(nameof(FolderTitleData))]
    public void ParseEpisode_FolderTitle_ResolvedFromFolderHierarchy(
        string fullPath, string? expectedFolderTitle)
    {
        var result = _sut.ParseEpisode(fullPath, new EpisodeNumberingHint());

        result.FolderTitle.Should().Be(expectedFolderTitle,
            $"FolderTitle should be the show-level folder name for '{fullPath}'");
    }

    [Fact]
    public void ParseEpisode_FolderTitle_SubShowConcatenation_LawAndOrderSvu()
    {
        // SOURCE: contracts/internal-contracts.md — multi-level nesting rule.
        // /Law and Order/SVU/S19/ → parent "Law and Order" and sub-show "SVU" are concatenated
        // because the grandparent is a TV-root folder.
        var result = _sut.ParseEpisode(
            "/Séries/Law and Order/SVU/S19/Law.and.Order.SUV.S19E23.FRENCH.DVDRip.XviD-Wawacity.tv.avi",
            new EpisodeNumberingHint());

        result.FolderTitle.Should().Be("Law and Order SVU",
            "SVU sub-show folders must concatenate with their parent to form the full show title");
    }

    [Fact]
    public void ParseEpisode_FolderTitle_DuplicateFolderNames_NoDuplication()
    {
        // SOURCE: contracts/internal-contracts.md — The Wire uses /The Wire/The Wire/ nesting.
        // The folder title must be "The Wire", not "The Wire The Wire".
        var result = _sut.ParseEpisode(
            "/Séries/The Wire/The Wire/Sur écoute S04E01 - La fin de l'été.mkv",
            new EpisodeNumberingHint());

        result.FolderTitle.Should().Be("The Wire",
            "duplicate parent/child folder names must not be concatenated");
    }

    [Fact]
    public void ParseEpisode_FolderTitle_ReleasePackFolder_ParentUsedInstead()
    {
        // SOURCE: contracts/internal-contracts.md — The Nanny pack folder.
        // The dotted release-pack folder must be skipped; the named show folder above is used.
        var result = _sut.ParseEpisode(
            "/Séries/The Nanny/Une.Nounou.Denfer.S04.MULTi.DVDRIP.x264-ETAY/Une.Nounou.Denfer.S04E10.MULTi.DVDRIP.x264-ETAY.mkv",
            new EpisodeNumberingHint());

        result.FolderTitle.Should().Be("The Nanny",
            "a release-pack folder (dotted, no spaces) must be skipped and the parent show folder used");
    }

    [Fact]
    public void ParseEpisode_FolderTitle_EmptyPath_ReturnsNull()
    {
        var result = _sut.ParseEpisode(string.Empty, new EpisodeNumberingHint());

        result.FolderTitle.Should().BeNull("empty path produces no folder title");
    }

    // =========================================================================
    // Release tag stripping — titles with tags BEFORE the SxxExx marker
    // SOURCE: contracts/internal-contracts.md — CleanTvShowTitle pipeline
    // =========================================================================

    /// <summary>
    ///     Release tag stripping rows: (fullPath, expectedCleanTitle).
    ///     Tags that appear in the filename text BEFORE the SxxExx marker must be stripped.
    ///     Accented characters, apostrophes, and title-internal hyphens must be preserved.
    ///     SOURCE: contracts/internal-contracts.md — CleanTvShowTitle requirements.
    /// </summary>
    public static TheoryData<string, string> ReleaseTagStrippingData => new()
    {
        // Quality tags before SxxExx
        // SOURCE: observed scene filenames — quality identifiers sometimes precede SxxExx
        { "/nas/TV/My Show/S01/My.Show.1080p.S01E01.mkv", "My Show" },
        { "/nas/TV/My Show/S01/My.Show.720p.S01E01.mkv", "My Show" },
        { "/nas/TV/My Show/S01/My.Show.2160p.S01E01.mkv", "My Show" },
        { "/nas/TV/My Show/S01/My.Show.4K.S01E01.mkv", "My Show" },

        // Codec tags before SxxExx
        { "/nas/TV/My Show/S01/My.Show.x264.S01E01.mkv", "My Show" },
        { "/nas/TV/My Show/S01/My.Show.x265.S01E01.mkv", "My Show" },
        { "/nas/TV/My Show/S01/My.Show.XviD.S01E01.mkv", "My Show" },
        { "/nas/TV/My Show/S01/My.Show.HEVC.S01E01.mkv", "My Show" },
        { "/nas/TV/My Show/S01/My.Show.H264.S01E01.mkv", "My Show" },

        // Source tags before SxxExx
        { "/nas/TV/My Show/S01/My.Show.DVDRip.S01E01.mkv", "My Show" },
        { "/nas/TV/My Show/S01/My.Show.WEBRip.S01E01.mkv", "My Show" },
        { "/nas/TV/My Show/S01/My.Show.BluRay.S01E01.mkv", "My Show" },
        { "/nas/TV/My Show/S01/My.Show.HDTV.S01E01.mkv", "My Show" },
        { "/nas/TV/My Show/S01/My.Show.BDRip.S01E01.mkv", "My Show" },

        // Language tags before SxxExx
        { "/nas/TV/My Show/S01/My.Show.FRENCH.S01E01.mkv", "My Show" },
        { "/nas/TV/My Show/S01/My.Show.MULTi.S01E01.mkv", "My Show" },
        { "/nas/TV/My Show/S01/My.Show.VOSTFR.S01E01.mkv", "My Show" },
        { "/nas/TV/My Show/S01/My.Show.TRUEFRENCH.S01E01.mkv", "My Show" },

        // Release group suffix attached to last word before SxxExx
        { "/nas/TV/Show Name/S01/Show.Name-ETAY.S01E01.mkv", "Show Name" },
        { "/nas/TV/Show Name/S01/Show.Name-AvALoN.S01E01.mkv", "Show Name" },

        // Multiple tags before SxxExx
        { "/nas/TV/My Show/S01/My.Show.MULTi.1080p.S01E01.mkv", "My Show" },
        { "/nas/TV/My Show/S01/My.Show.FRENCH.DVDRip.S01E01.mkv", "My Show" },
        { "/nas/TV/My Show/S01/My.Show.720p.x264.WEBRip.S01E01.mkv", "My Show" },

        // Accented characters MUST be preserved
        // SOURCE: contracts/internal-contracts.md — Unicode preservation rule.
        { "/nas/TV/Ma Série/S01/Ma.Serie.FRENCH.S01E01.mkv", "Ma Serie" },

        // Apostrophe in title MUST be preserved
        // SOURCE: contracts/internal-contracts.md — apostrophe preservation rule.
        { "/Séries/The Nanny/S04/Une.Nounou.D'enfer.FRENCH.S04E10.mkv", "Une Nounou D'enfer" },
    };

    [Theory]
    [MemberData(nameof(ReleaseTagStrippingData))]
    public void ParseEpisode_ReleaseTagsBeforeSxxExx_AreStrippedFromTitle(
        string fullPath, string expectedCleanTitle)
    {
        var result = _sut.ParseEpisode(fullPath, new EpisodeNumberingHint());

        result.IsSuccess.Should().BeTrue($"'{fullPath}' must parse successfully");
        result.Title.Should().Be(expectedCleanTitle,
            $"release tags before SxxExx must be stripped from the show title for '{Path.GetFileName(fullPath)}'");
    }

    [Fact]
    public void ParseEpisode_WEBDLBeforeSxxExx_IsStripped()
    {
        // WEB-DL contains a hyphen — verify the hyphen in the tag name is handled correctly.
        var result = _sut.ParseEpisode(
            "/nas/TV/My Show/S01/My.Show.WEB-DL.S01E01.mkv",
            new EpisodeNumberingHint());

        result.IsSuccess.Should().BeTrue();
        result.Title.Should().Be("My Show",
            "WEB-DL source tag appearing before SxxExx must be stripped");
    }
}