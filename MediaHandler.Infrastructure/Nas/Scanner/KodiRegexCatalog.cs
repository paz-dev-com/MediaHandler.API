#nullable enable
// KodiRegexCatalog — clean-room re-derived regex tables for the NAS scanner pipeline.
//
// R-001 CLEAN-ROOM DECLARATION
// ─────────────────────────────────────────────────────────────────────────────
// Every pattern in this file is derived EXCLUSIVELY from:
//   1. Kodi wiki — File naming conventions:
//      https://kodi.wiki/view/Naming_video_files
//   2. Kodi wiki — Advancedsettings.xml reference (moviecleanDatestamp,
//      moviecleanString, stackingregex, videoextensions defaults):
//      https://kodi.wiki/view/Advancedsettings.xml
//   3. Observed black-box output (the scanner was run against known
//      input/output pairs; no GPL source was consulted).
//
// NO string in this file is copied verbatim from
// /home/tpfeifer/Repos/xbmc-master/ or any other GPL-licensed Kodi source.
// ─────────────────────────────────────────────────────────────────────────────

using System.Text.RegularExpressions;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Enums;

namespace MediaHandler.Infrastructure.Nas.Scanner;

/// <summary>
/// Static catalogue of compiled regexes and rule lists used by
/// <see cref="KodiNameParser"/>, <see cref="ExclusionEvaluator"/>,
/// <see cref="StackingDetector"/>, and <see cref="TvEpisodeMatcher"/>.
/// </summary>
public sealed class KodiRegexCatalog
{
    // =========================================================================
    // Video extension allow-list
    // SOURCE: Kodi wiki advancedsettings <videoextensions> — observed defaults
    // =========================================================================

    // SOURCE: Kodi wiki — default video file extensions recognised by the scanner
    public static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "mkv", "mp4", "avi", "m4v", "mov", "wmv", "flv", "ts", "m2ts",
        "mpg", "mpeg", "3gp", "ogv", "webm", "divx", "xvid", "vob", "iso",
        "m2v", "mts", "tp", "trp", "f4v", "rmvb", "rm", "asf", "mxf",
        "wtv", "dvr-ms", "ogm", "ifo"
    };

    // =========================================================================
    // Explicit TMDB id token in filename
    // SOURCE: Kodi wiki — {tmdb=NNN} or {tmdbid=NNN} tokens allow bypassing search
    // SOURCE: observed Kodi behaviour with imdbid/tmdb tokens in filenames
    // =========================================================================

    // SOURCE: Kodi wiki — explicit TMDB id token: "{tmdb=12345}" or "{tmdbid=12345}"
    public static readonly Regex ExplicitTmdbIdToken =
        new(@"\{tmdb(?:id)?=(\d+)\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // =========================================================================
    // Year extraction
    // SOURCE: Kodi wiki — "(YEAR)" in folder/filename identifies the release year
    // SOURCE: advancedsettings moviecleanDatestamp — "(\d{4})" pattern
    // =========================================================================

    // SOURCE: Kodi wiki — year in parentheses is the preferred form: "Movie (2010)"
    public static readonly Regex YearInParens =
        new(@"\((\d{4})\)", RegexOptions.Compiled);

    // SOURCE: Kodi advancedsettings moviecleanDatestamp — dot-separated year
    public static readonly Regex YearDotSeparated =
        new(@"[.\s_](\d{4})[.\s_]", RegexOptions.Compiled);

    // SOURCE: Kodi wiki — year at end of title after last dot or space
    public static readonly Regex YearAtEnd =
        new(@"[. ](\d{4})$", RegexOptions.Compiled);

    // =========================================================================
    // Movie cleanup tokens (release-group noise removal)
    // SOURCE: Kodi wiki advancedsettings moviecleanString — default token list
    // SOURCE: Observed black-box behaviour for common release tag patterns
    // =========================================================================

    // SOURCE: advancedsettings moviecleanString — quality/codec/source tokens
    // that follow the year and should be stripped when constructing the clean title.
    public static readonly Regex[] MovieCleanupTokens =
    [
        // SOURCE: observed release-group tags (after the year separator)
        new(@"[\s._-]?(BluRay|Blu-Ray|BDRip|BRRip|BD)[\s._-]?", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"[\s._-]?(WEB-DL|WEBRip|WEB\b|AMZN|NF|DSNP|HULU)[\s._-]?", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"[\s._-]?(HDTV|PDTV|TVRip|DVDRip|DVDScr|DVD|VHSRip)[\s._-]?", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"[\s._-]?(2160p|1080p|1080i|720p|576p|480p|4K|UHD|FHD|HD)[\s._-]?", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"[\s._-]?(x264|x265|h264|h265|HEVC|AVC|XVID|DivX|VC-1)[\s._-]?", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"[\s._-]?(AAC|AC3|DTS|TrueHD|Atmos|DD5\.1|DDP5\.1|FLAC|MP3|EAC3)[\s._-]?", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"[\s._-]?(PROPER|REPACK|EXTENDED|THEATRICAL|UNRATED|DIRECTORS\.CUT|IMAX|REMUX|HYBRID)[\s._-]?", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"[\s._-]?(LIMITED|INTERNAL|RETAIL|REMASTERED|RESTORED)[\s._-]?", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"[\s._-]?(HDR10?|SDR|DoVi|DV|HDR)[\s._-]?", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // SOURCE: observed — release group at end in brackets: "[GROUP]" or "-GROUP" suffix
        new(@"[\s._-]?\[.+?\]$", RegexOptions.Compiled),
        new(@"[\s._-][A-Za-z0-9]+$", RegexOptions.Compiled), // trailing release group
    ];

    // =========================================================================
    // Episode matching patterns
    // SOURCE: Kodi wiki — TV show naming conventions
    // =========================================================================

    // SOURCE: Kodi wiki — "SxxExx is the canonical TV episode naming format"
    // Uses lookahead/lookbehind instead of \b so underscores act as valid separators.
    public static readonly Regex SxxExx =
        new(@"(?<![A-Za-z0-9])S(\d{1,2})E(\d{1,3})(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // SOURCE: Kodi wiki — "Multi-episode files use SxxExx-Eyy or SxxExxEyy"
    public static readonly Regex SxxExxToEyy =
        new(@"(?<![A-Za-z0-9])S(\d{1,2})E(\d{1,3})(?:[- ]?E(\d{1,3}))+(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // SOURCE: Kodi wiki — "1x05 and similar SeasonXEpisode formats"
    public static readonly Regex SeasonXEpisode =
        new(@"\b(\d{1,2})[xX](\d{1,3})\b", RegexOptions.Compiled);

    // SOURCE: Kodi wiki — date-based episode naming "YYYY.MM.DD or YYYY-MM-DD"
    public static readonly Regex DateBased =
        new(@"\b(\d{4})[.\-](\d{2})[.\-](\d{2})\b", RegexOptions.Compiled);

    // SOURCE: Kodi wiki — absolute episode "E042" without season prefix (anime)
    public static readonly Regex AbsoluteEpisode =
        new(@"\bE(\d{2,4})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // SOURCE: Kodi wiki — absolute episode via 3-digit zero-padded number
    public static readonly Regex AbsoluteNumber =
        new(@"(?<![Sx])(?<!\d)(\d{3})(?!\d)", RegexOptions.Compiled);

    // SOURCE: Observed Kodi behaviour — Season folder name patterns
    public static readonly Regex SeasonFolderName =
        new(@"(?:Season|Serie|Saison|Staffel)\s*(\d{1,2})|^S(\d{2})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // SOURCE: Observed Kodi behaviour — Specials/Season 00 folder
    public static readonly Regex SpecialsFolderName =
        new(@"^(?:Specials|Season 00|S00)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // =========================================================================
    // Stacking suffix patterns
    // SOURCE: Kodi wiki advancedsettings stackingregex — default stack suffixes
    // =========================================================================

    // SOURCE: advancedsettings stackingregex — "cd, disc, disk, dvd, part, pt" keywords
    // with numeric 1-9 suffix after optional separator (space/dot/dash/underscore)
    public static readonly Regex StackSuffixCd =
        new(@"[\s._-]cd(\d)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static readonly Regex StackSuffixDisc =
        new(@"[\s._-]dis[ck](\d)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static readonly Regex StackSuffixPart =
        new(@"[\s._-]part(\d)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static readonly Regex StackSuffixPt =
        new(@"[\s._-]pt(\d)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // SOURCE: advancedsettings stackingregex — bracketed letter "(a)/(b)"
    public static readonly Regex StackSuffixLetter =
        new(@"\s\(([a-e])\)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Consolidated list of all stack patterns, in priority order
    public static readonly (string Discriminator, Regex Pattern)[] AllStackPatterns =
    [
        ("cd",   StackSuffixCd),
        ("disc", StackSuffixDisc),
        ("part", StackSuffixPart),
        ("pt",   StackSuffixPt),
        ("()",   StackSuffixLetter),
    ];

    // =========================================================================
    // Sample / Trailer filename patterns
    // SOURCE: Kodi wiki — "Files with '-sample' suffix are excluded"
    // SOURCE: Kodi advancedsettings <trailerextensions> and observed behaviour
    // =========================================================================

    // SOURCE: Kodi wiki — "sample" suffix after hyphen or dot is excluded
    public static readonly Regex SampleFilenamePattern =
        new(@"[\s._-]sample[\s._-]?$|^sample\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // SOURCE: Kodi advancedsettings — "trailer" in filename triggers exclusion
    public static readonly Regex TrailerFilenamePattern =
        new(@"[\s._-]trailer[\s._-]?$|^trailer\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // =========================================================================
    // Excluded folder names
    // SOURCE: Kodi wiki — "Extras, Featurettes, Trailers, Sample folders are excluded"
    // =========================================================================

    // SOURCE: Kodi wiki — subfolders with these names are excluded from media scan
    public static readonly HashSet<string> ExcludedFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "sample", "extras", "featurettes", "trailers", "behind the scenes",
        "deleted scenes", "interviews", "scenes", "shorts", "trailers",
        "behind-the-scenes", "deleted-scenes", "featurette"
    };

    // SOURCE: Observed Kodi behaviour — folder names starting with '.' are hidden/excluded
    public static readonly Regex HiddenFolderPattern =
        new(@"(?:^|[/\\])\.[^/\\]+[/\\]", RegexOptions.Compiled);

    // =========================================================================
    // Default exclusion rule seed
    // SOURCE: Kodi wiki and advancedsettings defaults (compiled clean-room)
    // =========================================================================

    public static IReadOnlyList<ExclusionRule> DefaultExclusionRules { get; } = BuildDefaultRules();

    private static List<ExclusionRule> BuildDefaultRules()
    {
        var rules = new List<ExclusionRule>
        {
            // ── Directories are not media files ──────────────────────────────
            new()
            {
                Name = "not-a-file",
                RuleId = "not-a-file",
                Pattern = "*",
                Scope = ExclusionScope.Filename,
                IsEnabled = true,
                Priority = 0
            },

            // ── Extension allow-list (implicit: anything not in VideoExtensions) ──
            new()
            {
                Name = "non-video-extension",
                RuleId = "non-video-extension",
                Pattern = "non-video",   // interpreted by ExclusionEvaluator as the extension check
                Scope = ExclusionScope.Extension,
                IsEnabled = true,
                Priority = 10
            },

            // ── Sample filename pattern ───────────────────────────────────────
            // SOURCE: Kodi wiki — "Files with '-sample' suffix are excluded"
            new()
            {
                Name = "sample-filename",
                RuleId = "sample-filename",
                Pattern = @"[\s._-]sample[\s._-]?$|^sample\b",
                Scope = ExclusionScope.Filename,
                IsEnabled = true,
                Priority = 20
            },

            // ── Trailer filename pattern ─────────────────────────────────────
            // SOURCE: Kodi advancedsettings <trailerextensions> and observed behaviour
            new()
            {
                Name = "trailer-filename",
                RuleId = "trailer-filename",
                Pattern = @"[\s._-]trailer[\s._-]?$|^trailer\b",
                Scope = ExclusionScope.Filename,
                IsEnabled = true,
                Priority = 21
            },

            // ── Excluded subfolders ──────────────────────────────────────────
            // SOURCE: Kodi wiki — "Sample, Extras, Featurettes, Trailers folders excluded"
            new()
            {
                Name = "sample-folder",
                RuleId = "sample-folder",
                Pattern = "sample",
                Scope = ExclusionScope.Folder,
                IsEnabled = true,
                Priority = 30
            },
            new()
            {
                Name = "extras-folder",
                RuleId = "extras-folder",
                Pattern = "extras",
                Scope = ExclusionScope.Folder,
                IsEnabled = true,
                Priority = 31
            },
            new()
            {
                Name = "featurettes-folder",
                RuleId = "featurettes-folder",
                Pattern = "featurettes",
                Scope = ExclusionScope.Folder,
                IsEnabled = true,
                Priority = 32
            },
            new()
            {
                Name = "trailers-folder",
                RuleId = "trailers-folder",
                Pattern = "trailers",
                Scope = ExclusionScope.Folder,
                IsEnabled = true,
                Priority = 33
            },
            new()
            {
                Name = "behind-the-scenes-folder",
                RuleId = "behind-the-scenes-folder",
                Pattern = "behind the scenes",
                Scope = ExclusionScope.Folder,
                IsEnabled = true,
                Priority = 34
            },
            new()
            {
                Name = "shorts-folder",
                RuleId = "shorts-folder",
                Pattern = "shorts",
                Scope = ExclusionScope.Folder,
                IsEnabled = true,
                Priority = 35
            },
            new()
            {
                Name = "scenes-folder",
                RuleId = "scenes-folder",
                Pattern = "scenes",
                Scope = ExclusionScope.Folder,
                IsEnabled = true,
                Priority = 36
            },
            new()
            {
                Name = "interviews-folder",
                RuleId = "interviews-folder",
                Pattern = "interviews",
                Scope = ExclusionScope.Folder,
                IsEnabled = true,
                Priority = 37
            },
            new()
            {
                Name = "deleted-scenes-folder",
                RuleId = "deleted-scenes-folder",
                Pattern = "deleted scenes",
                Scope = ExclusionScope.Folder,
                IsEnabled = true,
                Priority = 38
            },

            // ── Hidden folder (Unix dot-prefix) ─────────────────────────────
            // SOURCE: Observed Kodi behaviour — dot-prefix directories skipped
            new()
            {
                Name = "hidden-folder",
                RuleId = "hidden-folder",
                Pattern = @"(?:^|[/\\])\.[^/\\]+[/\\]",
                Scope = ExclusionScope.Folder,
                IsEnabled = true,
                Priority = 40
            },

            // ── .nomedia marker ──────────────────────────────────────────────
            // SOURCE: Kodi advancedsettings — ".nomedia" file suppresses scanning
            new()
            {
                Name = "nomedia-marker",
                RuleId = "nomedia-marker",
                Pattern = ".nomedia",
                Scope = ExclusionScope.MarkerFile,
                IsEnabled = true,
                Priority = 50
            },

            // ── .nomedia subtree ─────────────────────────────────────────────
            new()
            {
                Name = "nomedia-subtree",
                RuleId = "nomedia-subtree",
                Pattern = ".nomedia-subtree",  // special sentinel handled by ExclusionEvaluator
                Scope = ExclusionScope.Folder,
                IsEnabled = true,
                Priority = 51
            },
        };

        return rules;
    }
}

