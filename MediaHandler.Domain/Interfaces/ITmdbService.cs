namespace MediaHandler.Domain.Interfaces;

public interface ITmdbService
{
    Task<TmdbMediaDto?> SearchMediaAsync(string query, string language, CancellationToken cancellationToken = default);
    Task<TmdbMediaDetailsDto?> GetMediaDetailsAsync(int tmdbId, string mediaType, string language, CancellationToken cancellationToken = default);
    Task<IEnumerable<TmdbSeasonDto>> GetTvShowSeasonsAsync(int tmdbId, string language, CancellationToken cancellationToken = default);
}

public record TmdbMediaDto(
    int Id,
    string Title,
    string? OriginalTitle,
    string? Overview,
    string MediaType,
    DateTime? ReleaseDate,
    string? PosterPath,
    string? BackdropPath,
    decimal? VoteAverage);

public record TmdbMediaDetailsDto(
    int Id,
    string Title,
    string? OriginalTitle,
    string? Overview,
    string MediaType,
    DateTime? ReleaseDate,
    int? Runtime,
    string? PosterPath,
    string? BackdropPath,
    decimal? VoteAverage,
    int? VoteCount,
    string? Genres,
    string Language);

public record TmdbSeasonDto(
    int SeasonNumber,
    string Name,
    string? Overview,
    DateTime? AirDate,
    string? PosterPath,
    int EpisodeCount,
    IEnumerable<TmdbEpisodeDto> Episodes);

public record TmdbEpisodeDto(
    int EpisodeNumber,
    string Name,
    string? Overview,
    DateTime? AirDate,
    string? StillPath,
    int? Runtime);
