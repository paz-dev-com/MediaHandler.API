using MediaHandler.Domain.Enums;

namespace MediaHandler.Application.Features.Media.DTOs;

public record MediaFileDto(
    Guid Id,
    string FilePath,
    long? FileSizeBytes,
    string? Format,
    string? Resolution);

public record MediaDto(
    Guid Id,
    int TmdbId,
    string Title,
    string? OriginalTitle,
    string? Overview,
    MediaType Type,
    DateTime? ReleaseDate,
    int? Runtime,
    string? PosterPath,
    string? BackdropPath,
    decimal? VoteAverage,
    IReadOnlyList<string> Genres,
    IReadOnlyList<MediaFileDto> Files,
    bool? IsWatched);

public record MediaListItemDto(
    Guid Id,
    int TmdbId,
    string Title,
    MediaType Type,
    DateTime? ReleaseDate,
    string? PosterPath,
    decimal? VoteAverage,
    int FileCount,
    bool? IsWatched);

public record MediaStatsDto(
    int TotalMedia,
    int Films,
    int TvShows,
    int WatchedByCurrentUser,
    int UnwatchedByCurrentUser,
    int TotalFiles,
    int UnlinkedFiles);
