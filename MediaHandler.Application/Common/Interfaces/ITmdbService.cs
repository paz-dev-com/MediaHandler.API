using MediaHandler.Application.Common.DTOs;
using MediaHandler.Domain.Enums;

namespace MediaHandler.Application.Common.Interfaces;

/// <summary>
///     Result of a TMDB id-based lookup (movie or TV show).
///     Used by the matcher precedence chain when an explicit TMDB id is already known.
/// </summary>
public record TmdbIdLookupResult(
    int TmdbId,
    MediaType Kind,
    string Title,
    int? Year,
    string? PosterPath);

/// <summary>
///     A single search candidate with popularity score, used by the matcher ambiguity policy.
/// </summary>
public record TmdbSearchCandidate(
    int TmdbId,
    MediaType Kind,
    string Title,
    int? Year,
    decimal PopularityScore,
    string? PosterPath);

public interface ITmdbService
{
    // ── Legacy search (used by existing TMDB import features) ─────────────────
    Task<TmdbMediaDto?> SearchMediaAsync(string query, string language, CancellationToken cancellationToken = default);

    Task<TmdbMediaDetailsDto?> GetMediaDetailsAsync(int tmdbId, string mediaType, string language,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<TmdbSeasonDto>> GetTvShowSeasonsAsync(int tmdbId, string language,
        CancellationToken cancellationToken = default);

    // ── Scanner: id-based lookups ──────────────────────────────────────────────
    /// <summary>Looks up a movie by its TMDB id. Returns null when the id does not exist.</summary>
    Task<TmdbIdLookupResult?> GetMovieByIdAsync(int tmdbId, string language = "en-US",
        CancellationToken cancellationToken = default);

    /// <summary>Looks up a TV show by its TMDB id. Returns null when the id does not exist.</summary>
    Task<TmdbIdLookupResult?> GetTvShowByIdAsync(int tmdbId, string language = "en-US",
        CancellationToken cancellationToken = default);

    // ── Scanner: multi-candidate search ───────────────────────────────────────
    /// <summary>
    ///     Searches TMDB and returns up to 5 candidates with popularity scores.
    ///     When <paramref name="year" /> is specified, filters to items within ±2 years of that value.
    ///     When <paramref name="kindHint" /> is specified, limits results to that media type.
    /// </summary>
    Task<IReadOnlyList<TmdbSearchCandidate>> SearchCandidatesAsync(
        string query,
        int? year,
        MediaType? kindHint,
        string language = "en-US",
        CancellationToken cancellationToken = default);
}