using MediaHandler.Application.Common.Models.Scanner;
using MediaHandler.Domain.Enums;

namespace MediaHandler.Application.Common.Interfaces;

/// <summary>
/// Clean-room re-implementation of Kodi's movie + episode filename parsing heuristics.
/// <para>
/// <b>R-001</b>: No verbatim copy of GPL Kodi source. All patterns are derived from
/// documented Kodi behaviour (Kodi Wiki: File naming) and black-box observation.
/// </para>
/// </summary>
public interface IKodiNameParser
{
    /// <summary>
    /// Parses a movie filename (or its containing folder name, which takes precedence)
    /// and extracts the canonical title and optionally the release year.
    /// </summary>
    /// <param name="fullPath">Absolute NAS path of the video file.</param>
    MovieNameParseResult ParseMovie(string fullPath);

    /// <summary>
    /// Parses a TV episode filename and extracts one or more season+episode numbers.
    /// Multi-episode files (e.g., <c>S02E05-E06</c>) return multiple <see cref="EpisodeNumber"/> items.
    /// </summary>
    /// <param name="fullPath">Absolute NAS path of the video file.</param>
    /// <param name="hint">Contextual hints derived from the library root and parent folder.</param>
    EpisodeNameParseResult ParseEpisode(string fullPath, EpisodeNumberingHint hint);
}

