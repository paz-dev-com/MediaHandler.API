namespace MediaHandler.Application.Common.DTOs;

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
    IReadOnlyList<string>? Genres,
    string Language,
    string? Status = null,
    int? NumberOfSeasons = null,
    int? NumberOfEpisodes = null);

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