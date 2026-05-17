using MediaHandler.Application.Common.Models;

namespace MediaHandler.Application.Common.Interfaces;

/// <summary>
///     Provides a reusable import service that checks for an existing <c>Media</c> entity
///     by <paramref name="tmdbId" /> before fetching metadata from TMDB and persisting a new one.
///     Encapsulates deduplication logic so it can be shared between the TMDB import command
///     and the auto-import pipeline.
/// </summary>
public interface IMediaImportService
{
    /// <summary>
    ///     Returns the <see cref="Guid" /> of the existing or newly created <c>Media</c> entity
    ///     that corresponds to the given TMDB identifier.
    /// </summary>
    /// <param name="tmdbId">The TMDB numeric identifier for the media.</param>
    /// <param name="mediaType">
    ///     The TMDB media type: <c>"movie"</c> or <c>"tv"</c>.
    /// </param>
    /// <param name="language">
    ///     The BCP-47 language tag used for TMDB metadata (e.g., <c>"en"</c>, <c>"fr"</c>).
    ///     Defaults to <c>"en"</c> when <c>null</c>.
    /// </param>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <returns>
    ///     A successful <see cref="Result{T}" /> containing the <c>Media.Id</c>,
    ///     or a failed result when TMDB returns no data for the given identifier.
    /// </returns>
    Task<Result<Guid>> ImportOrGetExistingAsync(int tmdbId, string mediaType, string? language, CancellationToken ct);
}