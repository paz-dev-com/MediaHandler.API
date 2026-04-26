namespace MediaHandler.Application.Common.Models.Scanner;

/// <summary>
/// Result of <c>IKodiNameParser.ParseMovie</c>.
/// Reflects the Kodi movie naming behavior: folder name takes precedence over filename.
/// </summary>
public record MovieNameParseResult(
    bool IsSuccess,
    string? Title,
    int? Year,
    string? Warning = null);

/// <summary>
/// A single season+episode coordinate extracted from a TV show filename.
/// Multi-episode files (S02E05-E06) produce two instances.
/// </summary>
public record EpisodeNumber(int Season, int Episode);

/// <summary>
/// Contextual hints supplied to <c>IKodiNameParser.ParseEpisode</c> / <c>ITvEpisodeMatcher.Match</c>
/// so that folder-derived season numbers can override ambiguous filename patterns.
/// </summary>
/// <param name="SeasonFromFolder">Season number inferred from the parent folder name (e.g., <c>Season 02</c>), or null.</param>
public record EpisodeNumberingHint(int? SeasonFromFolder = null);

/// <summary>
/// Result of <c>IKodiNameParser.ParseEpisode</c>.
/// Contains one or more <see cref="EpisodeNumber"/> for multi-episode files.
/// </summary>
public record EpisodeNameParseResult(
    bool IsSuccess,
    string? Title,
    IReadOnlyList<EpisodeNumber> EpisodeNumbers,
    string? Warning = null);

