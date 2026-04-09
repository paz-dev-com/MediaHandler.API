using MediaHandler.Application.Common.DTOs;
using MediaHandler.Domain.Entities;

namespace MediaHandler.Application.Common.Interfaces;

/// <summary>
/// Provides a reusable matching service that iterates over a set of unlinked
/// <see cref="MediaFile"/> records, parses each filename, searches TMDB, imports
/// (or retrieves) the matching <c>Media</c> entity, and sets <c>MediaFile.MediaId</c>.
/// Encapsulates the shared loop used by both the scan-and-import and the auto-import commands.
/// </summary>
public interface IMediaAutoMatchService
{
    /// <summary>
    /// Attempts to match every <see cref="MediaFile"/> in <paramref name="unlinkedFiles"/>
    /// against TMDB, then persists the link by setting <c>MediaFile.MediaId</c>.
    /// </summary>
    /// <param name="unlinkedFiles">
    /// The list of <see cref="MediaFile"/> records whose <c>MediaId</c> is <c>null</c>.
    /// </param>
    /// <param name="language">
    /// The BCP-47 language tag forwarded to TMDB API calls (e.g., <c>"en"</c>).
    /// </param>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <returns>
    /// An <see cref="AutoMatchResult"/> summarising how many files were matched,
    /// skipped (no TMDB result), or failed (exception during processing).
    /// </returns>
    Task<AutoMatchResult> MatchAndLinkUnlinkedFilesAsync(
        IReadOnlyList<MediaFile> unlinkedFiles,
        string language,
        CancellationToken ct);
}

