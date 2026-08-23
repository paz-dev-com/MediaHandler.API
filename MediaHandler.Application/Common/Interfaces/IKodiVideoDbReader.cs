using MediaHandler.Application.Common.Models.Kodi;

namespace MediaHandler.Application.Common.Interfaces;

/// <summary>
///     Reads a Kodi video database (<c>MyVideos&lt;version&gt;.db</c>) file.
///     The uploaded file is untrusted input: implementations must open it read-only,
///     execute only hardcoded SELECT statements, and stream results.
/// </summary>
public interface IKodiVideoDbReader
{
    /// <summary>
    ///     Validates that the file at <paramref name="filePath" /> is a supported Kodi video
    ///     database: version in the supported set, expected video-library tables and columns
    ///     present, and the file readable as SQLite.
    /// </summary>
    Task<KodiDbValidationResult> ValidateAsync(string filePath, int schemaVersion, CancellationToken ct = default);

    /// <summary>
    ///     Reads the full library snapshot (movies, shows with episodes, music videos) from a
    ///     previously validated file. Raw strings are returned unmodified — path normalization
    ///     is the translator's responsibility.
    /// </summary>
    Task<KodiLibrarySnapshot> ReadAsync(string filePath, int schemaVersion, CancellationToken ct = default);
}
