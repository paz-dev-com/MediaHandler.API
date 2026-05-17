// KodiNameParser — clean-room implementation of Kodi movie + episode name parsing.
//
// R-001 CLEAN-ROOM DECLARATION
// All patterns sourced from:
//   https://kodi.wiki/view/Naming_video_files/Movies
//   https://kodi.wiki/view/Naming_video_files/TV_shows
//   https://kodi.wiki/view/Advancedsettings.xml
// No GPL source code from /home/tpfeifer/Repos/xbmc-master/ was consulted.

using System.Text.RegularExpressions;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models.Scanner;
using Microsoft.Extensions.Options;

namespace MediaHandler.Infrastructure.Nas.Scanner;

/// <summary>
///     Clean-room re-implementation of Kodi's movie + episode filename parsing heuristics.
///     <para>
///         <b>Movie parsing rule</b>: When a movie file is inside a dedicated folder, the folder
///         name is used as the authoritative title source. The filename is parsed only as fallback.
///     </para>
/// </summary>
public sealed class KodiNameParser : IKodiNameParser
{
    // SOURCE: Kodi wiki — these generic folder names do not constitute a movie title.
    private static readonly HashSet<string> GenericFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "movies", "films", "video", "videos", "media", "content",
        "nas", "downloads", "torrents", "archive"
    };

    private readonly TvEpisodeMatcher _episodeMatcher = new();
    private readonly IOptionsMonitor<ReleaseTagOptions>? _releaseTagOptions;

    /// <summary>
    ///     Default constructor — no options injection. Existing callers that use
    ///     <c>new KodiNameParser()</c> continue to work unchanged; built-in
    ///     <see cref="KodiRegexCatalog.CleanTvShowTitle" /> patterns are applied.
    /// </summary>
    public KodiNameParser() { }

    /// <summary>
    ///     Constructor with optional <see cref="IOptionsMonitor{ReleaseTagOptions}" /> injection.
    ///     When provided, additional patterns from <c>Scanner:ReleaseTags:AdditionalPatterns</c>
    ///     configuration are merged with the built-in <see cref="KodiRegexCatalog.CleanTvShowTitle" /> set.
    /// </summary>
    public KodiNameParser(IOptionsMonitor<ReleaseTagOptions>? releaseTagOptions)
    {
        _releaseTagOptions = releaseTagOptions;
    }

    // =========================================================================
    // IKodiNameParser.ParseMovie
    // SOURCE: https://kodi.wiki/view/Naming_video_files/Movies
    // =========================================================================

    public MovieNameParseResult ParseMovie(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            return new MovieNameParseResult(false, null, null, "Empty path");

        fullPath = NormaliseSeparators(fullPath);
        var segments = fullPath.Split('/');
        if (segments.Length < 2)
            return ParseFromFilename(Path.GetFileNameWithoutExtension(fullPath));

        var filename = segments[^1];
        var folder = segments[^2];

        if (!IsGenericFolder(folder))
        {
            var folderResult = ParseFromFolderName(folder);
            if (folderResult.IsSuccess && folderResult.Title is not null)
                return folderResult;
        }

        return ParseFromFilename(Path.GetFileNameWithoutExtension(filename));
    }

    // =========================================================================
    // IKodiNameParser.ParseEpisode
    // SOURCE: https://kodi.wiki/view/Naming_video_files/TV_shows
    // =========================================================================

    public EpisodeNameParseResult ParseEpisode(string fullPath, EpisodeNumberingHint hint)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            return new EpisodeNameParseResult(false, null, [], "Empty path");

        var filename = Path.GetFileName(fullPath);
        var episodes = _episodeMatcher.Match(filename, hint);

        var folderTitle = ResolveShowFolderTitle(fullPath);

        if (episodes.Count == 0)
        {
            if (hint.SeasonFromFolder.HasValue)
                return new EpisodeNameParseResult(
                    true, ExtractShowTitle(fullPath),
                    [new EpisodeNumber(hint.SeasonFromFolder.Value, 0)],
                    "Episode number could not be determined from filename",
                    FolderTitle: folderTitle);

            return new EpisodeNameParseResult(false, ExtractShowTitle(fullPath), [],
                "No episode pattern found", FolderTitle: folderTitle);
        }

        var filenameNoExt = Path.GetFileNameWithoutExtension(filename);

        // Title carries the show name (text before SxxExx).
        // EpisodeTitle carries the text after SxxExx.
        // Year-like numbers before the SxxExx marker (e.g. "2011" in "Show.2011.S03E10")
        // are preserved in the show title for TMDB disambiguation.
        var showTitle = ExtractShowTitleFromFilename(filename, GetActiveTagPatterns());
        var episodeTitle = ExtractEpisodeTitle(filenameNoExt, episodes[0]);
        return new EpisodeNameParseResult(true, showTitle, episodes,
            EpisodeTitle: episodeTitle, FolderTitle: folderTitle);
    }

    /// <summary>
    ///     Extracts the TV show title from an episode filename by taking all text before
    ///     the first SxxExx marker, replacing dot/underscore separators with spaces, stripping
    ///     known release tags (quality, codec, source, language, release-group), and trimming.
    ///     Accented characters (é, è, ê…) and title-internal apostrophes are preserved by default
    ///     because .NET <see cref="Regex" /> is Unicode-aware and none of the built-in patterns
    ///     use <c>\w</c>-only anchors that would inadvertently strip non-ASCII code points.
    ///     Year-like numbers before the marker are intentionally kept as they aid TMDB disambiguation.
    /// </summary>
    /// <param name="filename">Filename only (not full path). Extension is stripped internally.</param>
    /// <param name="tagPatterns">
    ///     Optional override of the tag-stripping pattern set. When <see langword="null" />,
    ///     <see cref="KodiRegexCatalog.CleanTvShowTitle" /> is used (the built-in defaults).
    /// </param>
    /// <returns>The cleaned show title, or <c>null</c> if no SxxExx pattern is found.</returns>
    internal static string? ExtractShowTitleFromFilename(string filename, IReadOnlyList<Regex>? tagPatterns = null)
    {
        var filenameNoExt = Path.GetFileNameWithoutExtension(filename);
        var sxxMatch = KodiRegexCatalog.SxxExx.Match(filenameNoExt);

        if (!sxxMatch.Success)
            return null;

        var beforeSxx = filenameNoExt[..sxxMatch.Index];

        var title = beforeSxx
            .Replace('.', ' ')
            .Replace('_', ' ')
            .Trim(' ', '-');

        // Apply release-tag stripping patterns. Each pattern replaces a matched token with a
        // single space so that surrounding words remain correctly separated after cleanup.
        // Regex.Replace is Unicode-safe by default in .NET — accented characters (é, è, etc.)
        // and apostrophes (D'enfer) pass through unmodified.
        var patterns = tagPatterns ?? KodiRegexCatalog.CleanTvShowTitle;
        foreach (var pattern in patterns)
            title = pattern.Replace(title, " ");

        // Collapse multiple consecutive spaces introduced by stripping, then trim edges.
        title = Regex.Replace(title, @"\s{2,}", " ").Trim();

        return string.IsNullOrWhiteSpace(title) ? null : title;
    }

    /// <summary>
    ///     Returns the merged set of tag-stripping regexes: the built-in
    ///     <see cref="KodiRegexCatalog.CleanTvShowTitle" /> patterns plus any additional
    ///     patterns configured in <c>Scanner:ReleaseTags:AdditionalPatterns</c>.
    /// </summary>
    private IReadOnlyList<Regex> GetActiveTagPatterns()
    {
        var opts = _releaseTagOptions?.CurrentValue;
        if (opts is null || opts.AdditionalPatterns.Count == 0)
            return KodiRegexCatalog.CleanTvShowTitle;

        var merged = new List<Regex>(KodiRegexCatalog.CleanTvShowTitle);
        foreach (var pattern in opts.AdditionalPatterns)
            merged.Add(new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled));

        return merged;
    }

    /// <summary>
    ///     Resolves the TV show title by walking the folder hierarchy upward from the file path,
    ///     skipping season-level folders, TV-root library containers, and generic media folders.
    ///     Handles the sub-show concatenation pattern (e.g. /Law and Order/SVU/ → "Law and Order SVU")
    ///     and ignores dotted release-pack folders (e.g. Show.Name.S04.MULTi.DVDRIP.x264/).
    /// </summary>
    /// <param name="fullPath">Absolute path to the episode file.</param>
    /// <returns>The show-level folder name, or <c>null</c> if no usable folder is found.</returns>
    internal static string? ResolveShowFolderTitle(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            return null;

        var normalized = NormaliseSeparators(fullPath);
        var allSegments = normalized.Split('/');

        // Build a clean list of path segments: drop the filename (last) and empty entries.
        var segments = allSegments
            .Take(allSegments.Length - 1)
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();

        if (segments.Count == 0)
            return null;

        // Walk bottom-up to find the first usable show-level folder.
        var candidateIndex = -1;
        for (var i = segments.Count - 1; i >= 0; i--)
        {
            var seg = segments[i];

            // Stop when we reach a TV library root — too high up to be a show name.
            if (KodiRegexCatalog.TvRootFolderNames.Contains(seg))
                break;

            // Skip season folders, generic containers, and dotted release-pack folders.
            if (IsSeasonFolderSegment(seg)
                || KodiRegexCatalog.TvGenericFolderNames.Contains(seg)
                || IsReleasePackFolder(seg))
                continue;

            candidateIndex = i;
            break;
        }

        if (candidateIndex < 0)
            return null;

        var candidate = segments[candidateIndex];

        // Check parent for sub-show concatenation (e.g. /Law and Order/SVU/).
        if (candidateIndex == 0)
            return candidate;

        var parent = segments[candidateIndex - 1];

        // Parent must itself be a plain show-name folder (not root/season/generic/pack).
        if (string.IsNullOrEmpty(parent)
            || KodiRegexCatalog.TvRootFolderNames.Contains(parent)
            || IsSeasonFolderSegment(parent)
            || KodiRegexCatalog.TvGenericFolderNames.Contains(parent)
            || IsReleasePackFolder(parent))
            return candidate;

        // Avoid duplicating when the folder is nested inside a same-named parent
        // (e.g. /The Wire/The Wire/).
        if (string.Equals(parent, candidate, StringComparison.OrdinalIgnoreCase))
            return candidate;

        // Only concatenate when the grandparent is a TV-root, confirming a
        // deliberate sub-show layout (e.g. /Séries/Law and Order/SVU/).
        if (candidateIndex < 2)
            return candidate;

        var grandparent = segments[candidateIndex - 2];
        if (KodiRegexCatalog.TvRootFolderNames.Contains(grandparent)
            || string.IsNullOrEmpty(grandparent))
            return parent + " " + candidate;

        return candidate;
    }

    /// <summary>Returns <c>true</c> when <paramref name="segment" /> names a season-level folder.</summary>
    private static bool IsSeasonFolderSegment(string segment)
        => KodiRegexCatalog.SeasonFolderPattern.IsMatch(segment);

    /// <summary>
    ///     Returns <c>true</c> when <paramref name="segment" /> looks like a scene release-pack folder
    ///     (e.g. <c>Show.Name.S04.MULTi.DVDRIP.x264-ETAY</c>): no spaces but contains dots.
    ///     Legitimate show-folder names use spaces; batch/pack folders use dots as separators.
    /// </summary>
    private static bool IsReleasePackFolder(string segment)
        => !segment.Contains(' ') && segment.Contains('.');

    private static string NormaliseSeparators(string path)
    {
        return path.Replace('\\', '/');
    }

    private static bool IsGenericFolder(string folder)
    {
        return GenericFolderNames.Contains(folder);
    }

    private static MovieNameParseResult ParseFromFolderName(string folder)
    {
        // SOURCE: Kodi wiki — "(YEAR)" suffix in the folder name is the year
        var yearMatch = KodiRegexCatalog.YearInParens.Match(folder);
        if (yearMatch.Success)
        {
            var year = int.Parse(yearMatch.Groups[1].Value);
            // Strip the " (YEAR)" part from the title
            var title = folder[..yearMatch.Index].Trim(' ', '.', '_', '-');
            if (!string.IsNullOrWhiteSpace(title))
                return new MovieNameParseResult(true, CleanTitle(title), year);
        }

        // No year found in folder name; return the folder as the title if it
        // looks like a movie folder (longer than a generic name)
        var cleaned = CleanTitle(folder);
        return new MovieNameParseResult(string.IsNullOrWhiteSpace(cleaned) is false, cleaned, null);
    }

    private static MovieNameParseResult ParseFromFilename(string nameWithoutExt)
    {
        if (string.IsNullOrWhiteSpace(nameWithoutExt))
            return new MovieNameParseResult(false, null, null);

        // SOURCE: Kodi wiki — "(YEAR)" in filename
        var yearParenMatch = KodiRegexCatalog.YearInParens.Match(nameWithoutExt);
        if (yearParenMatch.Success)
        {
            var year = int.Parse(yearParenMatch.Groups[1].Value);
            var beforeYear = nameWithoutExt[..yearParenMatch.Index];
            var title = CleanTitle(beforeYear.Replace('.', ' ').Replace('_', ' ').Trim());
            if (!string.IsNullOrWhiteSpace(title))
                return new MovieNameParseResult(true, title, year);
        }

        // SOURCE: advancedsettings moviecleanDatestamp — dot-separated year in filename
        var yearDotMatch = FindYearInDottedFilename(nameWithoutExt);
        if (yearDotMatch.HasValue)
        {
            var (beforeYear, year) = yearDotMatch.Value;
            var title = CleanTitle(beforeYear.Replace('.', ' ').Replace('_', ' ').Trim());
            if (!string.IsNullOrWhiteSpace(title))
                return new MovieNameParseResult(true, title, year);
        }

        // No year found; clean up dots and return as title
        var fallbackTitle = CleanTitle(nameWithoutExt.Replace('.', ' ').Replace('_', ' ').Trim());
        return new MovieNameParseResult(
            string.IsNullOrWhiteSpace(fallbackTitle) is false, fallbackTitle, null);
    }

    private static (string BeforeYear, int Year)? FindYearInDottedFilename(string name)
    {
        // SOURCE: advancedsettings moviecleanDatestamp — year between dots/spaces
        // Scan right-to-left so the actual release year (typically after the title)
        // is preferred over year-like numbers that are part of the title itself
        // (e.g., "Blade.Runner.2049.2017" → year=2017, not 2049).
        var parts = name.Split('.', ' ', '_');
        for (var i = parts.Length - 1; i >= 1; i--)
            if (parts[i].Length == 4 && int.TryParse(parts[i], out var year)
                                     && year is >= 1888 and <= 2099)
            {
                var beforeYear = string.Join('.', parts[..i]);
                return (beforeYear, year);
            }

        return null;
    }

    private static string CleanTitle(string title)
    {
        // SOURCE: advancedsettings moviecleanString — strip common release-group tags
        // We only strip tags that appear AFTER the year is already removed;
        // so this is mostly for folder-fallback scenarios.
        title = title.Trim(' ', '.', '_', '-', '[', ']');
        return title;
    }

    private static string? ExtractShowTitle(string fullPath)
    {
        var normalized = NormaliseSeparators(fullPath);
        var segments = normalized.Split('/');
        // TV path is typically: /nas/TV/ShowName/Season XX/Episode.mkv
        // ShowName is usually 2 levels up from the file
        if (segments.Length >= 3)
            return segments[^3];
        if (segments.Length >= 2)
            return segments[^2];
        return null;
    }

    private static string? ExtractEpisodeTitle(string filenameNoExt, EpisodeNumber _)
    {
        // SOURCE: Kodi wiki — text after the SxxExx token is the episode title
        var sxxMatch = KodiRegexCatalog.SxxExx.Match(filenameNoExt);
        if (sxxMatch.Success)
        {
            var afterEp = filenameNoExt[(sxxMatch.Index + sxxMatch.Length)..].Trim('.', ' ', '_', '-');
            return string.IsNullOrWhiteSpace(afterEp) ? null : afterEp.Replace('.', ' ').Replace('_', ' ');
        }

        return null;
    }
}