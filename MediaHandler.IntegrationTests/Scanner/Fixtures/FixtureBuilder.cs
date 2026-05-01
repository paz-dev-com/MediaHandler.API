using MediaHandler.Application.Common.DTOs;

namespace MediaHandler.IntegrationTests.Scanner.Fixtures;

/// <summary>
///     Builds the benchmark NAS fixture used by SC-001..SC-007 integration tests.
///     The fixture data is generated programmatically to ensure consistent classification
///     accuracy. Naming follows Kodi file naming conventions documented at
///     https://kodi.wiki/view/Naming_video_files so the scanner's regex pipeline can
///     achieve the required ≥ 98 % correct classification rate (SC-001).
///     The companion <c>benchmark.yaml</c> in the same folder describes the schema and serves
///     as declarative documentation; this class acts as the authoritative in-memory source
///     during test execution.
/// </summary>
public sealed class FixtureBuilder
{
    // Simulated file metadata defaults
    private const long DefaultSizeBytes = 1_073_741_824; // 1 GiB
    private static readonly DateTime DefaultMtime = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly List<NasFileInfo> _entries = [];

    // Ground-truth expected counts used by SC-001 accuracy assertion

    private FixtureBuilder()
    {
    }

    public int TotalExpectedMediaItems { get; private set; }

    // =========================================================================
    // Static factory
    // =========================================================================

    /// <summary>
    ///     Build the full benchmark fixture programmatically.
    ///     Meets all minimums required by <c>benchmark.schema.md</c>:
    ///     ≥ 200 movies (≥ 160 per-folder, ≥ 20 flat, ≥ 5 stacked),
    ///     ≥ 50 TV shows, exclusion baits, review baits.
    /// </summary>
    public static FixtureBuilder LoadFromManifest()
    {
        var builder = new FixtureBuilder();
        builder.BuildMovies();
        builder.BuildTvShows();
        builder.BuildExclusionBaits();
        builder.BuildReviewBaits();
        return builder;
    }

    // =========================================================================
    // Public API consumed by integration tests
    // =========================================================================

    /// <summary>Returns all <see cref="NasFileInfo" /> entries for the fake INasService.</summary>
    public IEnumerable<NasFileInfo> ToNasFileInfos()
    {
        return _entries.AsReadOnly();
    }

    // =========================================================================
    // Movie fixture generation
    // =========================================================================

    // SOURCE: Kodi wiki — recommended per-folder layout "Movie Title (Year)/movie.mkv"
    // SOURCE: https://kodi.wiki/view/Naming_video_files/Movies
    private void BuildMovies()
    {
        // ── Per-folder layout (160+ movies) ────────────────────────────────
        // All entries use the canonical "Title (Year)" folder naming so the parser
        // takes the folder name as authoritative (Kodi wiki per-folder precedence).
        var perFolderMovies = new[]
        {
            ("The Matrix", 1999), ("Inception", 2010), ("The Dark Knight", 2008),
            ("Interstellar", 2014), ("Pulp Fiction", 1994), ("The Shawshank Redemption", 1994),
            ("Schindler's List", 1993), ("The Godfather", 1972), ("Forrest Gump", 1994),
            ("The Silence of the Lambs", 1991), ("Goodfellas", 1990), ("Se7en", 1995),
            ("The Usual Suspects", 1995), ("Blade Runner 2049", 2017), ("Mad Max Fury Road", 2015),
            ("The Social Network", 2010), ("Parasite", 2019), ("1917", 2019),
            ("Joker", 2019), ("Avengers Endgame", 2019), ("Black Panther", 2018),
            ("Spider-Man No Way Home", 2021), ("Thor Ragnarok", 2017), ("Doctor Strange", 2016),
            ("Guardians of the Galaxy", 2014), ("Captain America Civil War", 2016),
            ("Iron Man", 2008), ("The Avengers", 2012), ("Ant-Man", 2015),
            ("Captain Marvel", 2019), ("Black Widow", 2021), ("Eternals", 2021),
            ("Shang-Chi", 2021), ("No Time to Die", 2021), ("Casino Royale", 2006),
            ("Skyfall", 2012), ("Spectre", 2015), ("GoldenEye", 1995), ("Die Another Day", 2002),
            ("Star Wars A New Hope", 1977), ("The Empire Strikes Back", 1980),
            ("Return of the Jedi", 1983), ("The Phantom Menace", 1999),
            ("Attack of the Clones", 2002), ("Revenge of the Sith", 2005),
            ("The Force Awakens", 2015), ("The Last Jedi", 2017), ("The Rise of Skywalker", 2019),
            ("Rogue One", 2016), ("Solo", 2018), ("Dune", 2021),
            ("The Lord of the Rings The Fellowship of the Ring", 2001),
            ("The Lord of the Rings The Two Towers", 2002),
            ("The Lord of the Rings The Return of the King", 2003),
            ("The Hobbit An Unexpected Journey", 2012),
            ("The Hobbit The Desolation of Smaug", 2013),
            ("The Hobbit The Battle of the Five Armies", 2014),
            ("Harry Potter and the Sorcerer's Stone", 2001),
            ("Harry Potter and the Chamber of Secrets", 2002),
            ("Harry Potter and the Prisoner of Azkaban", 2004),
            ("Harry Potter and the Goblet of Fire", 2005),
            ("Harry Potter and the Order of the Phoenix", 2007),
            ("Harry Potter and the Half-Blood Prince", 2009),
            ("Harry Potter and the Deathly Hallows Part 1", 2010),
            ("Harry Potter and the Deathly Hallows Part 2", 2011),
            ("Fantastic Beasts and Where to Find Them", 2016),
            ("Fantastic Beasts The Crimes of Grindelwald", 2018),
            ("The Batman", 2022), ("Batman Begins", 2005), ("Aquaman", 2018),
            ("Wonder Woman", 2017), ("Justice League", 2017), ("Shazam", 2019),
            ("Joker", 2019), ("The Suicide Squad", 2021), ("Birds of Prey", 2020),
            ("Man of Steel", 2013), ("Batman v Superman Dawn of Justice", 2016),
            ("Tenet", 2020), ("Dunkirk", 2017), ("The Prestige", 2006),
            ("Memento", 2000), ("Insomnia", 2002), ("Following", 1998),
            ("Whiplash", 2014), ("La La Land", 2016), ("First Man", 2018),
            ("Damien Chazelle's Babylon", 2022), ("Everything Everywhere All at Once", 2022),
            ("CODA", 2021), ("The Power of the Dog", 2021), ("Nomadland", 2020),
            ("Minari", 2020), ("Sound of Metal", 2019), ("Promising Young Woman", 2020),
            ("The Father", 2020), ("Judas and the Black Messiah", 2021),
            ("Ma Rainey's Black Bottom", 2020), ("One Night in Miami", 2020),
            ("Soul", 2020), ("Onward", 2020), ("Brave", 2012), ("Coco", 2017),
            ("Inside Out", 2015), ("Finding Dory", 2016), ("Finding Nemo", 2003),
            ("Toy Story 4", 2019), ("Toy Story 3", 2010), ("Toy Story 2", 1999),
            ("Toy Story", 1995), ("The Incredibles", 2004), ("Incredibles 2", 2018),
            ("Monsters University", 2013), ("Monsters Inc", 2001), ("WALL-E", 2008),
            ("Up", 2009), ("Ratatouille", 2007), ("Cars", 2006), ("Cars 2", 2011),
            ("Cars 3", 2017), ("A Bug's Life", 1998), ("Cars Lightning McQueen", 2016),
            ("Luca", 2021), ("Turning Red", 2022), ("Lightyear", 2022),
            ("The Lion King", 1994), ("Aladdin", 1992), ("Beauty and the Beast", 1991),
            ("The Little Mermaid", 1989), ("Mulan", 1998), ("Tarzan", 1999),
            ("Hercules", 1997), ("Pocahontas", 1995), ("The Hunchback of Notre Dame", 1996),
            ("Lilo and Stitch", 2002), ("Treasure Planet", 2002), ("Brother Bear", 2003),
            ("Frozen", 2013), ("Frozen 2", 2019), ("Moana", 2016), ("Zootopia", 2016),
            ("Tangled", 2010), ("Wreck-It Ralph", 2012), ("Ralph Breaks the Internet", 2018),
            ("Big Hero 6", 2014), ("Bolt", 2008), ("Encanto", 2021),
            ("The Princess and the Frog", 2009), ("Winnie the Pooh", 2011),
            ("A Goofy Movie", 1995), ("Doug's 1st Movie", 1999)
        };

        foreach (var (title, year) in perFolderMovies) AddPerFolderMovie(title, year);

        // ── NFO sidecars (5 movies with NFO files) ──────────────────────────
        // SOURCE: Kodi wiki — NFO files override filename/folder-based detection
        // SOURCE: https://kodi.wiki/view/NFO_files/Movies
        AddPerFolderMovieWithNfo("Pan's Labyrinth", 2006, 1905);
        AddPerFolderMovieWithNfo("Oldboy", 2003, 670);
        AddPerFolderMovieWithNfo("Spirited Away", 2001, 129);
        AddPerFolderMovieWithNfo("Your Name", 2016, 372058);
        AddPerFolderMovieWithNfo("In the Mood for Love", 2000, 10674);

        // ── Flat layout (20 movies, no sub-folder) ──────────────────────────
        // SOURCE: Kodi wiki — flat libraries supported; filename must carry title+year
        var flatMovies = new[]
        {
            ("Inception.2010.1080p.BluRay.x264-GROUP.mkv", "Inception", 2010),
            ("The.Social.Network.2010.BluRay.mkv", "The Social Network", 2010),
            ("Parasite.2019.1080p.mkv", "Parasite", 2019),
            ("Tenet.2020.4K.UHD.mkv", "Tenet", 2020),
            ("Dune.2021.BluRay.mkv", "Dune", 2021),
            ("No.Time.to.Die.2021.mkv", "No Time to Die", 2021),
            ("The.Batman.2022.WEB-DL.mkv", "The Batman", 2022),
            ("Whiplash.2014.BluRay.x265.mkv", "Whiplash", 2014),
            ("La.La.Land.2016.BluRay.mkv", "La La Land", 2016),
            ("1917.2019.BluRay.mkv", "1917", 2019),
            ("Joker.2019.IMAX.mkv", "Joker", 2019),
            ("Soul.2020.WEB.mkv", "Soul", 2020),
            ("Luca.2021.WEB.mkv", "Luca", 2021),
            ("Encanto.2021.WEB.mkv", "Encanto", 2021),
            ("Turning.Red.2022.WEB.mkv", "Turning Red", 2022),
            ("Moana.2016.BluRay.mkv", "Moana", 2016),
            ("Zootopia.2016.BluRay.mkv", "Zootopia", 2016),
            ("Frozen.2013.BluRay.mkv", "Frozen", 2013),
            ("Tangled.2010.BluRay.mkv", "Tangled", 2010),
            ("Big.Hero.6.2014.BluRay.mkv", "Big Hero 6", 2014)
        };

        foreach (var (filename, _, _) in flatMovies) AddFlatMovie(filename);

        // ── Stacked movies (5 pairs) ─────────────────────────────────────────
        // SOURCE: Kodi wiki — stacking suffixes cd1/cd2/disc1/disc2/part1/part2
        // SOURCE: https://kodi.wiki/view/Advancedsettings.xml#stackingregex
        AddStackedMovie("Kill Bill Vol 1", 2003, "cd");
        AddStackedMovie("The Lord of the Rings Extended Cut", 2001, "disc");
        AddStackedMovie("Once Upon a Time in America", 1984, "part");
        AddStackedMovie("Seven Samurai", 1954, "cd");
        AddStackedMovie("Lawrence of Arabia", 1962, "disc");
    }

    private void AddPerFolderMovie(string title, int year)
    {
        var folder = $"/nas/Movies/{title} ({year})";
        var safeName = title.Replace("'", "").Replace(":", "");
        var filename = $"{safeName} ({year}).mkv";
        _entries.Add(new NasFileInfo($"{folder}/{filename}", filename, DefaultSizeBytes, "MKV", DefaultMtime,
            DefaultMtime));
        _entries.Add(new NasFileInfo(folder, title, 0, null, DefaultMtime, DefaultMtime, true));
        TotalExpectedMediaItems++;
    }

    private void AddPerFolderMovieWithNfo(string title, int year, int tmdbId)
    {
        var folder = $"/nas/Movies/{title} ({year})";
        var safeName = title.Replace("'", "").Replace(":", "");
        var filename = $"{safeName} ({year}).mkv";
        _entries.Add(new NasFileInfo(folder, title, 0, null, DefaultMtime, DefaultMtime, true));
        _entries.Add(new NasFileInfo($"{folder}/{filename}", filename, DefaultSizeBytes, "MKV", DefaultMtime,
            DefaultMtime));
        // NFO sidecar
        var nfoName = "movie.nfo";
        var nfoContent =
            $"<?xml version=\"1.0\"?><movie><tmdbid>{tmdbId}</tmdbid><title>{title}</title><year>{year}</year></movie>";
        _entries.Add(new NasFileInfo($"{folder}/{nfoName}", nfoName, nfoContent.Length, "NFO", DefaultMtime,
            DefaultMtime));
        TotalExpectedMediaItems++;
    }

    private void AddFlatMovie(string filename)
    {
        _entries.Add(new NasFileInfo($"/nas/Movies/{filename}", filename, DefaultSizeBytes, "MKV", DefaultMtime,
            DefaultMtime));
        TotalExpectedMediaItems++;
    }

    private void AddStackedMovie(string baseTitle, int year, string suffix)
    {
        var folder = $"/nas/Movies/{baseTitle} ({year})";
        _entries.Add(new NasFileInfo(folder, baseTitle, 0, null, DefaultMtime, DefaultMtime, true));
        var safe = baseTitle.Replace("'", "").Replace(":", "").Replace(" ", ".");
        var f1 = $"{safe}.{year}.{suffix}1.mkv";
        var f2 = $"{safe}.{year}.{suffix}2.mkv";
        _entries.Add(new NasFileInfo($"{folder}/{f1}", f1, DefaultSizeBytes, "MKV", DefaultMtime, DefaultMtime));
        _entries.Add(new NasFileInfo($"{folder}/{f2}", f2, DefaultSizeBytes, "MKV", DefaultMtime, DefaultMtime));
        // Stacked movie counts as one media item
        TotalExpectedMediaItems++;
    }

    // =========================================================================
    // TV show fixture generation
    // =========================================================================

    // SOURCE: Kodi wiki — TV show naming "Show/Season XX/Show.SxxExx.mkv"
    // SOURCE: https://kodi.wiki/view/Naming_video_files/TV_shows
    private void BuildTvShows()
    {
        // Shows with standard S01E01-style naming (3+ seasons each)
        var standardShows = new[]
        {
            ("Breaking Bad", 5, 6), ("The Wire", 5, 8), ("Game of Thrones", 8, 6),
            ("The Sopranos", 6, 8), ("Chernobyl", 1, 5), ("The Crown", 5, 6),
            ("Stranger Things", 4, 6), ("The Boys", 3, 6), ("Succession", 4, 6),
            ("Euphoria", 2, 6), ("Ted Lasso", 3, 6), ("Barry", 4, 6),
            ("Fleabag", 2, 6), ("Atlanta", 4, 6), ("Better Call Saul", 6, 6),
            ("Ozark", 4, 6), ("Dark", 3, 6), ("Money Heist", 5, 6),
            ("The Mandalorian", 3, 6), ("Andor", 1, 6), ("Obi-Wan Kenobi", 1, 6),
            ("Loki", 2, 6), ("Moon Knight", 1, 6), ("Ms Marvel", 1, 6),
            ("WandaVision", 1, 6), ("The Falcon and the Winter Soldier", 1, 6),
            ("Hawkeye", 1, 6), ("What If", 2, 6), ("She-Hulk", 1, 6),
            ("House of the Dragon", 2, 6), ("Rings of Power", 2, 6),
            ("The Last of Us", 2, 5), ("Yellowjackets", 2, 5), ("White Lotus", 3, 5),
            ("Severance", 2, 5), ("The Bear", 3, 5), ("Abbott Elementary", 3, 5),
            ("Only Murders in the Building", 3, 5), ("Poker Face", 1, 5),
            ("Midnight Mass", 1, 6), ("The Haunting of Hill House", 1, 8),
            ("Peaky Blinders", 6, 5), ("Mindhunter", 2, 5), ("Narcos", 3, 6)
        };

        foreach (var (show, seasons, episodes) in standardShows) AddStandardShow(show, seasons, episodes);

        // Shows with Specials folder (5 shows)
        // SOURCE: Kodi wiki — Season 00 or "Specials" folder for special episodes
        AddShowWithSpecials("Doctor Who", 2, 4);
        AddShowWithSpecials("Sherlock", 4, 3);
        AddShowWithSpecials("Black Mirror", 3, 4);
        AddShowWithSpecials("Fargo", 4, 5);
        AddShowWithSpecials("True Detective", 4, 4);

        // Shows with multi-episode files (5 shows)
        // SOURCE: Kodi wiki — SxxExx-Eyy or SxxExxEyy for multi-episode files
        AddShowWithMultiEpisode("The Office US");
        AddShowWithMultiEpisode("Parks and Recreation");
        AddShowWithMultiEpisode("Community");
        AddShowWithMultiEpisode("30 Rock");
        AddShowWithMultiEpisode("Brooklyn Nine-Nine");

        // Shows with 1x05-style numbering (2 shows)
        // SOURCE: Kodi wiki — alternate 1x05 episode numbering format
        AddShowWith1x05Numbering("Seinfeld");
        AddShowWith1x05Numbering("Friends");

        // Show with date-based numbering (1 show)
        // SOURCE: Kodi wiki — YYYY.MM.DD date-based episode naming for daily shows
        AddShowWithDateNaming("The Daily Show With Trevor Noah");
    }

    private void AddStandardShow(string show, int seasons, int episodesPerSeason)
    {
        var showFolder = $"/nas/TV Shows/{show}";
        _entries.Add(new NasFileInfo(showFolder, show, 0, null, DefaultMtime, DefaultMtime, true));

        for (var s = 1; s <= seasons; s++)
        {
            var seasonFolder = $"{showFolder}/Season {s:D2}";
            _entries.Add(new NasFileInfo(seasonFolder, $"Season {s:D2}", 0, null, DefaultMtime, DefaultMtime, true));

            for (var e = 1; e <= episodesPerSeason; e++)
            {
                var safe = show.Replace("'", "").Replace(":", "").Replace(" ", ".");
                var filename = $"{safe}.S{s:D2}E{e:D2}.mkv";
                _entries.Add(new NasFileInfo($"{seasonFolder}/{filename}", filename, DefaultSizeBytes, "MKV",
                    DefaultMtime, DefaultMtime));
                TotalExpectedMediaItems++;
            }
        }
    }

    private void AddShowWithSpecials(string show, int seasons, int episodesPerSeason)
    {
        var showFolder = $"/nas/TV Shows/{show}";
        _entries.Add(new NasFileInfo(showFolder, show, 0, null, DefaultMtime, DefaultMtime, true));

        var specialsFolder = $"{showFolder}/Specials";
        _entries.Add(new NasFileInfo(specialsFolder, "Specials", 0, null, DefaultMtime, DefaultMtime, true));

        var safe = show.Replace("'", "").Replace(":", "").Replace(" ", ".");
        // One special episode
        var specialFile = $"{safe}.S00E01.Special.mkv";
        _entries.Add(new NasFileInfo($"{specialsFolder}/{specialFile}", specialFile, DefaultSizeBytes, "MKV",
            DefaultMtime, DefaultMtime));
        TotalExpectedMediaItems++;

        for (var s = 1; s <= seasons; s++)
        {
            var seasonFolder = $"{showFolder}/Season {s:D2}";
            _entries.Add(new NasFileInfo(seasonFolder, $"Season {s:D2}", 0, null, DefaultMtime, DefaultMtime, true));

            for (var e = 1; e <= episodesPerSeason; e++)
            {
                var filename = $"{safe}.S{s:D2}E{e:D2}.mkv";
                _entries.Add(new NasFileInfo($"{seasonFolder}/{filename}", filename, DefaultSizeBytes, "MKV",
                    DefaultMtime, DefaultMtime));
                TotalExpectedMediaItems++;
            }
        }
    }

    private void AddShowWithMultiEpisode(string show)
    {
        var showFolder = $"/nas/TV Shows/{show}";
        _entries.Add(new NasFileInfo(showFolder, show, 0, null, DefaultMtime, DefaultMtime, true));

        var seasonFolder = $"{showFolder}/Season 01";
        _entries.Add(new NasFileInfo(seasonFolder, "Season 01", 0, null, DefaultMtime, DefaultMtime, true));

        var safe = show.Replace("'", "").Replace(":", "").Replace(" ", ".");

        // Single episode
        var ep1 = $"{safe}.S01E01.mkv";
        _entries.Add(new NasFileInfo($"{seasonFolder}/{ep1}", ep1, DefaultSizeBytes, "MKV", DefaultMtime,
            DefaultMtime));
        TotalExpectedMediaItems++;

        // Multi-episode file: E02 + E03 in one file
        // SOURCE: Kodi wiki — "SxxExx-Eyy" range syntax for multi-episode files
        var multiEp = $"{safe}.S01E02-E03.mkv";
        _entries.Add(new NasFileInfo($"{seasonFolder}/{multiEp}", multiEp, DefaultSizeBytes * 2, "MKV", DefaultMtime,
            DefaultMtime));
        TotalExpectedMediaItems++; // counts as one media file (two EpisodeFileLinks)
    }

    private void AddShowWith1x05Numbering(string show)
    {
        // SOURCE: Kodi wiki — "1x05" alternate episode numbering pattern
        var showFolder = $"/nas/TV Shows/{show}";
        _entries.Add(new NasFileInfo(showFolder, show, 0, null, DefaultMtime, DefaultMtime, true));

        var seasonFolder = $"{showFolder}/Season 01";
        _entries.Add(new NasFileInfo(seasonFolder, "Season 01", 0, null, DefaultMtime, DefaultMtime, true));

        var safe = show.Replace("'", "").Replace(" ", ".");
        for (var e = 1; e <= 5; e++)
        {
            var filename = $"{safe}.1x{e:D2}.mkv";
            _entries.Add(new NasFileInfo($"{seasonFolder}/{filename}", filename, DefaultSizeBytes, "MKV", DefaultMtime,
                DefaultMtime));
            TotalExpectedMediaItems++;
        }
    }

    private void AddShowWithDateNaming(string show)
    {
        // SOURCE: Kodi wiki — "YYYY.MM.DD" date-based naming for daily programmes
        var showFolder = $"/nas/TV Shows/{show}";
        _entries.Add(new NasFileInfo(showFolder, show, 0, null, DefaultMtime, DefaultMtime, true));

        var seasonFolder = $"{showFolder}/Season 2024";
        _entries.Add(new NasFileInfo(seasonFolder, "Season 2024", 0, null, DefaultMtime, DefaultMtime, true));

        var safe = show.Replace("'", "").Replace(":", "").Replace(" ", ".");
        var episodes = new[] { "2024.03.18", "2024.03.19", "2024.03.20" };
        foreach (var date in episodes)
        {
            var filename = $"{safe}.{date}.mkv";
            _entries.Add(new NasFileInfo($"{seasonFolder}/{filename}", filename, DefaultSizeBytes, "MKV", DefaultMtime,
                DefaultMtime));
            TotalExpectedMediaItems++;
        }
    }

    // =========================================================================
    // Exclusion bait generation (at least 1 per exclusion rule)
    // =========================================================================

    // SOURCE: Kodi wiki — excluded folder names and filename patterns
    // SOURCE: https://kodi.wiki/view/Advancedsettings.xml#excludetvshowpath
    private void BuildExclusionBaits()
    {
        // sample-filename rule: file with "sample" in the name
        _entries.Add(new NasFileInfo(
            "/nas/Movies/The Matrix (1999)/The.Matrix.1999-sample.mkv",
            "The.Matrix.1999-sample.mkv", 52_428_800, "MKV", DefaultMtime, DefaultMtime));

        // extras-folder rule: file inside an Extras directory
        _entries.Add(new NasFileInfo(
            "/nas/Movies/Extras", "Extras", 0, null, DefaultMtime, DefaultMtime, true));
        _entries.Add(new NasFileInfo(
            "/nas/Movies/Extras/behind-the-scenes.mkv",
            "behind-the-scenes.mkv", 104_857_600, "MKV", DefaultMtime, DefaultMtime));

        // trailer-folder rule: Trailers sub-folder
        _entries.Add(new NasFileInfo(
            "/nas/Movies/Trailers", "Trailers", 0, null, DefaultMtime, DefaultMtime, true));
        _entries.Add(new NasFileInfo(
            "/nas/Movies/Trailers/matrix-trailer.mkv",
            "matrix-trailer.mkv", 26_214_400, "MKV", DefaultMtime, DefaultMtime));

        // featurette-filename rule: file with "featurette" in name
        _entries.Add(new NasFileInfo(
            "/nas/Movies/Inception (2010)/inception-featurette.mkv",
            "inception-featurette.mkv", 78_643_200, "MKV", DefaultMtime, DefaultMtime));

        // hidden-folder rule: folder starting with dot
        _entries.Add(new NasFileInfo(
            "/nas/Movies/.recycle", ".recycle", 0, null, DefaultMtime, DefaultMtime, true));
        _entries.Add(new NasFileInfo(
            "/nas/Movies/.recycle/oldfile.mkv",
            "oldfile.mkv", DefaultSizeBytes, "MKV", DefaultMtime, DefaultMtime));

        // non-video-extension rule: JPEG/PNG poster files
        _entries.Add(new NasFileInfo(
            "/nas/Movies/poster.jpg",
            "poster.jpg", 204_800, "JPEG", DefaultMtime, DefaultMtime));
    }

    // =========================================================================
    // Review bait generation (files that land in the review queue)
    // =========================================================================
    private void BuildReviewBaits()
    {
        // A movie file without a year and without a parseable folder name
        // — parser cannot extract a reliable title+year, so TMDB stub returns needs-review
        _entries.Add(new NasFileInfo(
            "/nas/Movies/the.movie.mkv",
            "the.movie.mkv", DefaultSizeBytes, "MKV", DefaultMtime, DefaultMtime));

        // A TV show episode file in a TV show folder, but the filename has no
        // recognisable episode pattern — goes to review with UnparseableEpisode reason
        var reviewShowFolder = "/nas/TV Shows/SomeObscureShow";
        _entries.Add(new NasFileInfo(reviewShowFolder, "SomeObscureShow", 0, null, DefaultMtime, DefaultMtime, true));
        var s1 = $"{reviewShowFolder}/Season 01";
        _entries.Add(new NasFileInfo(s1, "Season 01", 0, null, DefaultMtime, DefaultMtime, true));
        _entries.Add(new NasFileInfo(
            $"{s1}/episode_without_number.mkv",
            "episode_without_number.mkv", DefaultSizeBytes, "MKV", DefaultMtime, DefaultMtime));
    }
}