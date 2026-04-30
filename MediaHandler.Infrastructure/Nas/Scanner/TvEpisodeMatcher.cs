#nullable enable
// TvEpisodeMatcher — clean-room implementation of Kodi-equivalent TV episode number extraction.
//
// R-001 CLEAN-ROOM DECLARATION
// Episode patterns sourced from:
//   https://kodi.wiki/view/Naming_video_files/TV_shows
//   https://kodi.wiki/view/Advancedsettings.xml (episoderegex defaults)
//   Observed black-box Kodi default behaviour.
// No GPL source from /home/tpfeifer/Repos/xbmc-master/ was consulted.

using System.Text.RegularExpressions;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models.Scanner;

namespace MediaHandler.Infrastructure.Nas.Scanner;

/// <summary>
/// Extracts season+episode numbers from TV episode filenames using
/// Kodi-compatible pattern matching.
/// </summary>
public sealed class TvEpisodeMatcher : ITvEpisodeMatcher
{
    public IReadOnlyList<EpisodeNumber> Match(string filename, EpisodeNumberingHint hint)
    {
        if (string.IsNullOrWhiteSpace(filename))
            return [];

        var nameNoExt = System.IO.Path.GetFileNameWithoutExtension(filename);

        // SOURCE: Kodi wiki — "SxxExx is the canonical TV episode naming format"
        // Check for SxxExxEyy or SxxExx-Eyy multi-episode patterns first
        var multiMatch = KodiRegexCatalog.SxxExxToEyy.Match(nameNoExt);
        if (multiMatch.Success)
        {
            return ExtractMultiEpisode(nameNoExt, multiMatch);
        }

        var sxxMatch = KodiRegexCatalog.SxxExx.Match(nameNoExt);
        if (sxxMatch.Success)
        {
            var season = int.Parse(sxxMatch.Groups[1].Value);
            var episode = int.Parse(sxxMatch.Groups[2].Value);
            return [new EpisodeNumber(season, episode)];
        }

        // ── 2. SeasonXEpisode (1x05 / 1X05 / 2x12 style) ────────────────────
        // SOURCE: Kodi wiki — "1x05 is an alternate naming format"
        var sxEpMatch = KodiRegexCatalog.SeasonXEpisode.Match(nameNoExt);
        if (sxEpMatch.Success)
        {
            var season = int.Parse(sxEpMatch.Groups[1].Value);
            var episode = int.Parse(sxEpMatch.Groups[2].Value);
            return [new EpisodeNumber(season, episode)];
        }

        // ── 3. Date-based YYYY.MM.DD or YYYY-MM-DD ───────────────────────────
        // SOURCE: Kodi wiki — "YYYY-MM-DD date-based episode naming"
        var dateMatch = KodiRegexCatalog.DateBased.Match(nameNoExt);
        if (dateMatch.Success)
        {
            var year = int.Parse(dateMatch.Groups[1].Value);
            var month = int.Parse(dateMatch.Groups[2].Value);
            var day = int.Parse(dateMatch.Groups[3].Value);
            if (IsValidDate(year, month, day))
            {
                var episodeOrdinal = new DateTime(year, month, day).DayOfYear;
                return [new EpisodeNumber(year, episodeOrdinal)];
            }
        }

        // ── 4. Absolute episode "E042" (anime) ───────────────────────────────
        // SOURCE: Kodi wiki — absolute episode numbers for anime (season=0)
        var absEMatch = KodiRegexCatalog.AbsoluteEpisode.Match(nameNoExt);
        if (absEMatch.Success)
        {
            var episodeNum = int.Parse(absEMatch.Groups[1].Value);
            var season = hint.SeasonFromFolder ?? 0;
            // If we have a season hint and an absolute episode, use the hint season
            return [new EpisodeNumber(season, episodeNum)];
        }

        // ── 5. Three-digit absolute number (anime fallback) ──────────────────
        // SOURCE: Observed Kodi behaviour — zero-padded 3-digit number
        var absNumMatch = KodiRegexCatalog.AbsoluteNumber.Match(nameNoExt);
        if (absNumMatch.Success && int.TryParse(absNumMatch.Groups[1].Value, out var absNum))
        {
            return [new EpisodeNumber(0, absNum)];
        }

        // ── 6. No pattern found ───────────────────────────────────────────────
        return [];
    }

    private static IReadOnlyList<EpisodeNumber> ExtractMultiEpisode(string nameNoExt, Match multiMatch)
    {
        // SOURCE: Kodi wiki — "SxxExx-Eyy or SxxExxEyy yields multiple episode rows"
        // Extract all E\d+ tokens within the matched text to handle all variants.
        var season = int.Parse(multiMatch.Groups[1].Value);
        var matchedText = multiMatch.Value; // e.g., "S02E05-E06" or "S02E05E06"

        var epMatches = Regex.Matches(matchedText, @"E(\d{1,3})", RegexOptions.IgnoreCase);
        var episodes = epMatches
            .Select(m => new EpisodeNumber(season, int.Parse(m.Groups[1].Value)))
            .ToList();

        if (episodes.Count > 0)
            return episodes;

        // Fallback: check for range pattern S01E01-E03 in the full string and expand it
        var rangeMatch = Regex.Match(nameNoExt,
            @"S(\d{1,2})E(\d{1,3})-E(\d{1,3})", RegexOptions.IgnoreCase);
        if (rangeMatch.Success)
        {
            var s = int.Parse(rangeMatch.Groups[1].Value);
            var epStart = int.Parse(rangeMatch.Groups[2].Value);
            var epEnd = int.Parse(rangeMatch.Groups[3].Value);
            return Enumerable.Range(epStart, epEnd - epStart + 1)
                .Select(ep => new EpisodeNumber(s, ep))
                .ToList()
                .AsReadOnly();
        }

        return [new EpisodeNumber(season, int.Parse(multiMatch.Groups[2].Value))];
    }

    private static bool IsValidDate(int year, int month, int day)
    {
        try
        {
            _ = new DateTime(year, month, day);
            return year is >= 1900 and <= 2099;
        }
        catch
        {
            return false;
        }
    }
}

