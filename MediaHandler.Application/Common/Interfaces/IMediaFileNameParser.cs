using MediaHandler.Application.Common.DTOs;

namespace MediaHandler.Application.Common.Interfaces;

/// <summary>
///     Parses a NAS file path to extract structured media metadata
///     (title, release year, and media type hint) for use in TMDB search queries.
/// </summary>
public interface IMediaFileNameParser
{
    /// <summary>
    ///     Attempts to extract a media title, optional year, and optional media type hint
    ///     from a file path.
    /// </summary>
    /// <param name="filePath">
    ///     The full file path as returned by the NAS service
    ///     (e.g., <c>/Movies/The.Matrix.1999.1080p.mkv</c>).
    /// </param>
    /// <returns>
    ///     A <see cref="ParsedMediaInfo" /> record when parsing succeeds,
    ///     or <c>null</c> when the path cannot yield a usable title.
    /// </returns>
    ParsedMediaInfo? Parse(string filePath);
}