namespace MediaHandler.Application.Features.Episodes.DTOs;

public record TvSeasonDto(
    Guid Id,
    int SeasonNumber,
    string Name,
    string? Overview,
    DateTime? AirDate,
    string? PosterPath,
    int EpisodeCount,
    int WatchedCount,
    IReadOnlyList<TvEpisodeDto> Episodes);

public record TvEpisodeDto(
    Guid Id,
    int EpisodeNumber,
    string Name,
    string? Overview,
    DateTime? AirDate,
    string? StillPath,
    int? Runtime,
    bool IsWatched);