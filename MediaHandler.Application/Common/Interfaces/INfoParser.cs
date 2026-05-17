using MediaHandler.Application.Common.Models.Scanner;

namespace MediaHandler.Application.Common.Interfaces;

/// <summary>
///     Parses Kodi-compatible <c>.nfo</c> sidecar files and extracts structured metadata.
///     Malformed NFO files return a <see cref="NfoParseResult.Malformed" /> result — they do
///     NOT throw, allowing the pipeline to fall back to filename parsing gracefully.
/// </summary>
public interface INfoParser
{
    /// <summary>
    ///     Reads and parses the NFO file at <paramref name="nfoPath" />.
    /// </summary>
    /// <param name="nfoPath">Absolute NAS path of the <c>.nfo</c> file.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<NfoParseResult> ParseAsync(string nfoPath, CancellationToken ct = default);
}