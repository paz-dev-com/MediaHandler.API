using MediaHandler.Domain.Enums;

namespace MediaHandler.Application.Features.Dashboard.DTOs;

/// <summary>
///     Data-transfer object representing a computed TV show group for the admin dashboard.
///     Groups are computed on-the-fly from <see cref="MediaHandler.Domain.Entities.ScanItemDecision" /> rows
///     and are <b>not</b> persisted to the database.
/// </summary>
public record TvShowGroupDto(
    Guid GroupId,
    string ParsedShowName,
    int EpisodeCount,
    int? AssignedTmdbId,
    MediaType? AssignedTmdbKind,
    string? AssignedTitle,
    int? AssignedYear,
    string? AssignedPosterPath);

