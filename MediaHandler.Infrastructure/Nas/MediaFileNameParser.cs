using System.Text.RegularExpressions;
using MediaHandler.Application.Common;
using MediaHandler.Application.Common.DTOs;
using MediaHandler.Application.Common.Interfaces;

namespace MediaHandler.Infrastructure.Nas;

/// <summary>
/// Parses NAS file paths to extract a media title, optional release year,
/// and optional TMDB media-type hint (<c>"movie"</c> or <c>"tv"</c>).
/// </summary>
/// <remarks>
/// <para>Supported filename patterns:</para>
/// <list type="bullet">
///   <item>Dot-separated: <c>The.Matrix.1999.1080p.BluRay.mkv</c></item>
///   <item>Year in folder: <c>The Matrix (1999)/The.Matrix.mkv</c></item>
///   <item>TV show patterns: <c>breaking.bad.s01e01.720p.mkv</c></item>
///   <item>Plain title: <c>Inception.mkv</c></item>
/// </list>
/// </remarks>
public sealed class MediaFileNameParser : IMediaFileNameParser
{
    // Video file extensions we recognize as media — single source of truth in MediaFileConstants
    private static readonly HashSet<string> VideoExtensions = MediaFileConstants.VideoExtensions;

    // Path segments that indicate a movie folder
    private static readonly string[] MovieSegments =
        ["movies", "films", "movie", "film"];

    // Path segments that indicate a TV show folder
    private static readonly string[] TvSegments =
        ["series", "séries", "tv", "tv shows", "tvshows", "shows", "anime"];

    // Strips quality/codec/source tags and everything after them
    // e.g., "The Matrix 1999 1080p BluRay x264" → "The Matrix 1999"
    private static readonly Regex QualityTagRegex = new(
        @"\b(?:2160p|1080p|1080i|720p|720i|480p|576p|4k|uhd|hdr|sdr|" +
        @"bluray|blu-ray|bdrip|brrip|web-dl|webdl|webrip|web|hdtv|dvdrip|dvd|" +
        @"hdrip|xvid|divx|x264|x265|hevc|avc|h264|h265|aac|ac3|dts|mp3|" +
        @"truehd|atmos|dd5\.1|remux|repack|proper|extended|theatrical|directors\.cut|" +
        @"unrated|limited|retail|internal).*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Strips release-group tags: [YTS], [YIFY], {RARBG}, (GROUP), etc.
    private static readonly Regex GroupTagRegex = new(
        @"[\[\({][^\]\)}{]{1,20}[\]\)}]",
        RegexOptions.Compiled);

    // Year pattern: 4 digits between 1900 and 2099
    private static readonly Regex YearRegex = new(
        @"\b((?:19|20)\d{2})\b",
        RegexOptions.Compiled);

    // TV episode patterns: S01E01, S01E01E02, 1x01, etc.
    private static readonly Regex TvEpisodeRegex = new(
        @"\bS\d{1,2}E\d{1,2}|\d{1,2}x\d{2}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Season folder pattern: "Season 01", "Saison 2", etc.
    private static readonly Regex SeasonFolderRegex = new(
        @"^(?:season|saison|serie)\s*\d+$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <inheritdoc />
    public ParsedMediaInfo? Parse(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        // Normalise separators
        var normalised = filePath.Replace('\\', '/');

        var segments = normalised.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return null;

        var fileName = segments[^1];
        var ext = Path.GetExtension(fileName);
        if (!VideoExtensions.Contains(ext))
            return null;

        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);

        // Detect media type from path segments
        var mediaTypeHint = DetectMediaType(segments);

        // If it looks like a TV show (episode pattern in filename), override hint
        if (TvEpisodeRegex.IsMatch(nameWithoutExt))
            mediaTypeHint = "tv";

        // --- Title extraction strategy ---
        // 1. If the parent folder contains a year (e.g., "The Matrix (1999)"), prefer it
        // 2. Otherwise fall back to parsing the filename itself

        string? title = null;
        int? year = null;

        if (segments.Length >= 2)
        {
            var parentFolder = segments[^2];

            // Skip season-level folders and go one level up
            if (SeasonFolderRegex.IsMatch(parentFolder) && segments.Length >= 3)
                parentFolder = segments[^3];

            var folderResult = ExtractTitleAndYear(parentFolder);
            if (folderResult.Title is not null)
            {
                title = folderResult.Title;
                year = folderResult.Year;
            }
        }

        // Fall back to filename when folder didn't yield a title
        if (title is null)
        {
            var fileResult = ExtractTitleAndYear(nameWithoutExt);
            title = fileResult.Title;
            year = fileResult.Year;
        }

        // If we still have an episode pattern in the source, strip it from title
        if (title is not null)
            title = TvEpisodeRegex.Replace(title, string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(title))
            return null;

        // Normalise spacing
        title = Regex.Replace(title, @"\s{2,}", " ").Trim();

        return new ParsedMediaInfo(title, year, mediaTypeHint);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string? DetectMediaType(string[] segments)
    {
        foreach (var segment in segments)
        {
            var lower = segment.ToLowerInvariant();
            if (MovieSegments.Contains(lower)) return "movie";
            if (TvSegments.Contains(lower)) return "tv";
        }

        return null;
    }

    private static (string? Title, int? Year) ExtractTitleAndYear(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (null, null);

        // Replace dots and underscores with spaces (common in filenames)
        var spaced = raw.Replace('.', ' ').Replace('_', ' ');

        // Remove group tags
        spaced = GroupTagRegex.Replace(spaced, " ");

        // Extract year before stripping it
        int? year = null;
        var yearMatch = YearRegex.Match(spaced);
        if (yearMatch.Success && int.TryParse(yearMatch.Value, out var y))
            year = y;

        // Truncate at the year position (everything after year is usually tags)
        if (yearMatch.Success)
            spaced = spaced[..yearMatch.Index];

        // Strip quality/codec tags
        spaced = QualityTagRegex.Replace(spaced, string.Empty);

        // Clean up extra punctuation and whitespace
        spaced = Regex.Replace(spaced, @"[-–_]+", " ");
        spaced = Regex.Replace(spaced, @"\s{2,}", " ").Trim();

        if (string.IsNullOrWhiteSpace(spaced))
            return (null, year);

        // Title-case the result
        var title = System.Globalization.CultureInfo.InvariantCulture.TextInfo
            .ToTitleCase(spaced.ToLowerInvariant());

        return (title, year);
    }
}

