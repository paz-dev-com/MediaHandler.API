// GetEnrichmentRunDetails — query and handler returning per-media enrichment details
// for a specific enrichment run.
//
// Parses EnrichedMediaIdsJson from the EnrichmentRun row to rebuild the per-entry breakdown,
// enriched with Media title/type and MediaFile names from the database.

using System.Text.Json;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Dashboard.DTOs;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Dashboard.Queries.GetEnrichmentRunDetails;

/// <summary>Query to retrieve per-media enrichment details for a specific run.</summary>
public record GetEnrichmentRunDetailsQuery(Guid RunId)
    : IRequest<Result<List<EnrichmentMediaDetailDto>>>;

// =========================================================================
// Handler
// =========================================================================

/// <summary>
///     Handles <see cref="GetEnrichmentRunDetailsQuery" />.
///     <list type="bullet">
///         <item>Loads the <c>EnrichmentRun</c> row by <c>RunId</c>.</item>
///         <item>Parses <c>EnrichedMediaIdsJson</c> to recover per-media status entries.</item>
///         <item>Batch-loads associated <c>Media</c> and <c>MediaFile</c> rows.</item>
///         <item>Merges <c>ErrorDetailsJson</c> for the <c>error</c> field on failed entries.</item>
///         <item>Returns a flat <c>List&lt;EnrichmentMediaDetailDto&gt;</c>.</item>
///     </list>
/// </summary>
public sealed class GetEnrichmentRunDetailsQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetEnrichmentRunDetailsQuery, Result<List<EnrichmentMediaDetailDto>>>
{
    // Internal record mirroring the EnrichmentMediaResult persisted by EnrichmentCoordinator
    private record MediaResultEntry(Guid MediaId, string Status);

    public async Task<Result<List<EnrichmentMediaDetailDto>>> Handle(
        GetEnrichmentRunDetailsQuery request,
        CancellationToken cancellationToken)
    {
        // Load the enrichment run
        var run = await db.EnrichmentRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == request.RunId, cancellationToken);

        if (run is null)
            return Result.Fail<List<EnrichmentMediaDetailDto>>(
                $"ENRICHMENT_RUN_NOT_FOUND: EnrichmentRun '{request.RunId}' was not found.");

        // Parse per-media processing results
        List<MediaResultEntry> mediaResults = [];
        if (!string.IsNullOrEmpty(run.EnrichedMediaIdsJson))
        {
            try
            {
                mediaResults = JsonSerializer.Deserialize<List<MediaResultEntry>>(
                    run.EnrichedMediaIdsJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? [];
            }
            catch
            {
                mediaResults = [];
            }
        }

        // Parse error details for failed entries (keyed by MediaId)
        var errorsByMediaId = new Dictionary<Guid, string>();
        if (!string.IsNullOrEmpty(run.ErrorDetailsJson))
        {
            try
            {
                var errors = JsonSerializer.Deserialize<List<EnrichmentErrorDetailDto>>(
                    run.ErrorDetailsJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? [];

                foreach (var err in errors)
                    errorsByMediaId[err.MediaId] = err.Error;
            }
            catch
            {
                // ignore — error detail is best-effort
            }
        }

        if (mediaResults.Count == 0)
            return Result.Success(new List<EnrichmentMediaDetailDto>());

        // Batch-load all referenced Media rows
        var mediaIds = mediaResults.Select(r => r.MediaId).Distinct().ToList();

        var mediaLookup = await db.Medias
            .AsNoTracking()
            .Where(m => mediaIds.Contains(m.Id))
            .Select(m => new { m.Id, m.TmdbId, m.Title, m.Type })
            .ToDictionaryAsync(m => m.Id, cancellationToken);

        // Batch-load all MediaFile rows for the referenced media entries
        var filesByMediaId = await db.MediaFiles
            .AsNoTracking()
            .Where(f => f.MediaId != null && mediaIds.Contains(f.MediaId!.Value))
            .GroupBy(f => f.MediaId!.Value)
            .Select(g => new
            {
                MediaId = g.Key,
                FileCount = g.Count(),
                FileNames = g.Select(f => f.FilePath).ToList()
            })
            .ToDictionaryAsync(g => g.MediaId, cancellationToken);

        // Build result
        var details = mediaResults.Select(entry =>
        {
            mediaLookup.TryGetValue(entry.MediaId, out var media);
            filesByMediaId.TryGetValue(entry.MediaId, out var files);
            errorsByMediaId.TryGetValue(entry.MediaId, out var error);

            var fileNames = files?.FileNames
                .Select(path => System.IO.Path.GetFileName(path))
                .ToList()
                ?? [];

            return new EnrichmentMediaDetailDto(
                MediaId: entry.MediaId,
                TmdbId: media?.TmdbId,
                Title: media?.Title,
                Type: media?.Type == MediaType.TvShow ? "TvShow" : "Film",
                Status: entry.Status,
                FileCount: files?.FileCount ?? 0,
                FileNames: fileNames,
                Error: error);
        }).ToList();

        return Result.Success(details);
    }
}

