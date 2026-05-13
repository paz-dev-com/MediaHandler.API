namespace MediaHandler.Application.Features.Dashboard.DTOs;

/// <summary>
///     Per-media entry detail for an enrichment run, returned by
///     <c>GET /api/v1/admin/enrichment/{runId}/details</c>.
/// </summary>
/// <param name="MediaId">The internal media row identifier.</param>
/// <param name="TmdbId">TMDB identifier of the media entry.</param>
/// <param name="Title">Title of the media entry at the time of enrichment.</param>
/// <param name="Type">Media type: <c>Film</c> or <c>TvShow</c>.</param>
/// <param name="Status">Processing outcome: <c>Enriched</c>, <c>Failed</c>, or <c>Skipped</c>.</param>
/// <param name="FileCount">Number of media files associated with this entry.</param>
/// <param name="FileNames">List of file names associated with this entry.</param>
/// <param name="Error">Error message when <paramref name="Status" /> is <c>Failed</c>; otherwise <c>null</c>.</param>
public record EnrichmentMediaDetailDto(
    Guid MediaId,
    int? TmdbId,
    string? Title,
    string Type,
    string Status,
    int FileCount,
    IReadOnlyList<string> FileNames,
    string? Error);

