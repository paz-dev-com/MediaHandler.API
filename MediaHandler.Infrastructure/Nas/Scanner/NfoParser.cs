#nullable enable
// NfoParser — XDocument-based Kodi NFO sidecar file parser.
// Reads and parses .nfo XML files that accompany media files in Kodi-organised libraries.
// Designed to be fault-tolerant: malformed or unreadable files return NfoParseResult.Malformed
// rather than throwing, allowing the scan pipeline to fall back to filename-based detection.
//
// R-001 CLEAN-ROOM DECLARATION
// NFO file format derived from documented Kodi behaviour:
//   https://kodi.wiki/view/NFO_files/Movies
//   https://kodi.wiki/view/NFO_files/TV_shows
// No GPL source was consulted or copied.

using System.Xml.Linq;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models.Scanner;

namespace MediaHandler.Infrastructure.Nas.Scanner;

/// <summary>
/// Production implementation of <see cref="INfoParser"/>.
/// Uses <see cref="XDocument"/> to parse Kodi-style NFO sidecar files and extract
/// structured metadata (title, year, TMDB id, IMDB id, season, episode).
///
/// Tolerant of unknown XML elements — only well-known fields are extracted; all others
/// are silently ignored. Malformed XML or unreadable files return
/// <see cref="NfoParseResult"/>.<see cref="NfoParseResult.Malformed"/> rather than throwing.
/// </summary>
public sealed class NfoParser : INfoParser
{
    /// <inheritdoc />
    public async Task<NfoParseResult> ParseAsync(string nfoPath, CancellationToken ct = default)
    {
        string content;
        try
        {
            content = await File.ReadAllTextAsync(nfoPath, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return NfoParseResult.Malformed($"Cannot read NFO file at '{nfoPath}': {ex.Message}");
        }

        return ParseContent(content);
    }

    // =========================================================================
    // Internal XML parsing — separated for testability
    // =========================================================================

    /// <summary>
    /// Parses raw NFO XML content into a <see cref="NfoParseResult"/>.
    /// Called by <see cref="ParseAsync"/> after the file has been read.
    /// Exposed internally for direct testing without file I/O.
    /// </summary>
    internal static NfoParseResult ParseContent(string xmlContent)
    {
        if (string.IsNullOrWhiteSpace(xmlContent))
            return NfoParseResult.Malformed("NFO file is empty");

        XDocument doc;
        try
        {
            // LoadOptions.None: do not preserve whitespace nodes — avoids false positives on trim
            doc = XDocument.Parse(xmlContent, LoadOptions.None);
        }
        catch (Exception ex)
        {
            return NfoParseResult.Malformed($"Invalid XML in NFO file: {ex.Message}");
        }

        var root = doc.Root;
        if (root is null)
            return NfoParseResult.Malformed("NFO XML contains no root element");

        // Extract all recognised fields, ignoring unknown elements silently.
        // SOURCE: Kodi wiki NFO schemas for movies, TV shows, and episodes.
        var title   = root.Element("title")?.Value?.Trim().NullIfEmpty();
        var year    = TryParseInt(root.Element("year")?.Value);
        var tmdbId  = TryParseInt(root.Element("tmdbid")?.Value);
        // IMDB id: <imdbid> preferred; <id> is the legacy Kodi element name
        var imdbId  = (root.Element("imdbid")?.Value ?? root.Element("id")?.Value)?.Trim().NullIfEmpty();
        var season  = TryParseInt(root.Element("season")?.Value);
        var episode = TryParseInt(root.Element("episode")?.Value);

        return new NfoParseResult(
            ParsedSuccessfully: true,
            Title: title,
            Year: year,
            TmdbId: tmdbId,
            ImdbId: imdbId,
            Season: season,
            Episode: episode);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static int? TryParseInt(string? value) =>
        int.TryParse(value?.Trim(), out var n) ? n : null;
}

file static class StringExtensions
{
    /// <summary>Returns null when the string is null or consists only of whitespace.</summary>
    internal static string? NullIfEmpty(this string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}

