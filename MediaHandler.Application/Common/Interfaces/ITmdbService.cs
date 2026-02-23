using MediaHandler.Application.Common.DTOs;

namespace MediaHandler.Application.Common.Interfaces;

public interface ITmdbService
{
    Task<TmdbMediaDto?> SearchMediaAsync(string query, string language, CancellationToken cancellationToken = default);
    Task<TmdbMediaDetailsDto?> GetMediaDetailsAsync(int tmdbId, string mediaType, string language, CancellationToken cancellationToken = default);
    Task<IEnumerable<TmdbSeasonDto>> GetTvShowSeasonsAsync(int tmdbId, string language, CancellationToken cancellationToken = default);
}
