using MediaHandler.Application.Common.Models.Scanner;

namespace MediaHandler.Application.Common.Interfaces;

/// <summary>
/// Extracts one or more season+episode coordinates from a TV episode filename.
/// Supports all patterns recognised by Kodi's default episode regex catalogue:
/// SxxExx, SxxExx-Eyy, xXy, 1x05, YYYY.MM.DD, absolute-numbering fallback.
/// </summary>
public interface ITvEpisodeMatcher
{
    /// <summary>
    /// Matches episode number(s) from <paramref name="filename"/>.
    /// </summary>
    /// <param name="filename">Bare filename (no directory component).</param>
    /// <param name="hint">Contextual hints that help resolve ambiguous patterns.</param>
    /// <returns>
    /// One item for single-episode files; two or more for multi-episode files
    /// (e.g., S02E05-E06); empty list when no recognisable pattern is found.
    /// </returns>
    IReadOnlyList<EpisodeNumber> Match(string filename, EpisodeNumberingHint hint);
}

