#nullable enable
// KodiNameParser — clean-room implementation of Kodi movie + episode name parsing.
//
// R-001 CLEAN-ROOM DECLARATION
// All patterns sourced from:
//   https://kodi.wiki/view/Naming_video_files/Movies
//   https://kodi.wiki/view/Naming_video_files/TV_shows
//   https://kodi.wiki/view/Advancedsettings.xml
// No GPL source code from /home/tpfeifer/Repos/xbmc-master/ was consulted.

using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models.Scanner;
using MediaHandler.Domain.Enums;

namespace MediaHandler.Infrastructure.Nas.Scanner;

/// <summary>
/// Clean-room re-implementation of Kodi's movie + episode filename parsing heuristics.
/// <para>
/// <b>Movie parsing rule</b> (SOURCE: Kodi wiki "File naming / Movies"):
/// When a movie file is inside a dedicated folder, the folder name is used as the
/// authoritative title source. The filename is parsed only as a fallback.
/// </para>
/// </summary>
public sealed class KodiNameParser : IKodiNameParser
{
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
            return ParseFromFilename(System.IO.Path.GetFileNameWithoutExtension(fullPath));

        // SOURCE: Kodi wiki — the folder containing the movie file is tried first.
        // The folder is the parent directory of the file.
        var filename = segments[^1]; // last segment
        var folder = segments[^2];   // parent folder

        // If the parent folder looks like it contains a year (i.e., it's a movie-specific
        // folder like "Inception (2010)" rather than a generic folder like "Movies"),
        // treat it as the authoritative source.
        // SOURCE: Kodi wiki — "The recommended naming scheme is 'Movie Title (Year)'"
        if (!IsGenericFolder(folder))
        {
            var folderResult = ParseFromFolderName(folder);
            if (folderResult.IsSuccess && folderResult.Title is not null)
                return folderResult;
        }

        // Fallback to filename without extension
        return ParseFromFilename(System.IO.Path.GetFileNameWithoutExtension(filename));
    }

    // =========================================================================
    // IKodiNameParser.ParseEpisode
    // SOURCE: https://kodi.wiki/view/Naming_video_files/TV_shows
    // =========================================================================

    public EpisodeNameParseResult ParseEpisode(string fullPath, EpisodeNumberingHint hint)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            return new EpisodeNameParseResult(false, null, [], "Empty path");

        // Pass the full filename (with extension) to the matcher so it can strip internally.
        // Do NOT strip here — the matcher already calls GetFileNameWithoutExtension.
        var filename = System.IO.Path.GetFileName(fullPath);
        var episodes = _episodeMatcher.Match(filename, hint);

        if (episodes.Count == 0)
        {
            // When the season is known from the containing folder but no episode number
            // is in the filename, classify the file as an episode in that season with
            // episode=0 (unknown). This lets the scanner flag it for review rather than
            // silently discarding it.
            // SOURCE: Observed Kodi behaviour — folder context provides season when filename lacks it.
            if (hint.SeasonFromFolder.HasValue)
                return new EpisodeNameParseResult(
                    true, ExtractShowTitle(fullPath),
                    [new EpisodeNumber(hint.SeasonFromFolder.Value, 0)],
                    "Episode number could not be determined from filename");

            return new EpisodeNameParseResult(false, ExtractShowTitle(fullPath), [], "No episode pattern found");
        }

        var filenameNoExt = System.IO.Path.GetFileNameWithoutExtension(filename);
        var title = ExtractEpisodeTitle(filenameNoExt, episodes[0]);
        return new EpisodeNameParseResult(true, title, episodes);
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    private readonly TvEpisodeMatcher _episodeMatcher = new();

    private static string NormaliseSeparators(string path) =>
        path.Replace('\\', '/');

    // SOURCE: Kodi wiki — these generic folder names do not constitute a movie title.
    private static readonly HashSet<string> GenericFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "movies", "films", "video", "videos", "media", "content",
        "nas", "downloads", "torrents", "archive"
    };

    private static bool IsGenericFolder(string folder) =>
        GenericFolderNames.Contains(folder);

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
        var parts = name.Split(['.', ' ', '_']);
        for (var i = parts.Length - 1; i >= 1; i--)
        {
            if (parts[i].Length == 4 && int.TryParse(parts[i], out var year)
                && year is >= 1888 and <= 2099)
            {
                var beforeYear = string.Join('.', parts[..i]);
                return (beforeYear, year);
            }
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

    private static string? ExtractEpisodeTitle(string filenameNoExt, EpisodeNumber firstEp)
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

