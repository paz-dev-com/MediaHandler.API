// ListScanDecisions — paginated query for the scan decisions browser.
// Returns all ScanItemDecision rows for a given ScanRun with optional filters,
// joining MediaFile → Media to populate assignedTitle/Year/PosterPath,
// and LibraryRoot to populate libraryRootPath.

using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using MediaHandler.Application.Common.DTOs;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Dashboard.DTOs;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Dashboard.Queries.ListScanDecisions;

/// <summary>
///     Query parameters for listing <c>ScanItemDecision</c> rows for a scan run,
///     with optional filters by decision type, media type, and library root.
/// </summary>
public record ListScanDecisionsQuery(
    Guid ScanRunId,
    ScanDecisionKind? DecisionType,
    MediaType? MediaType,
    Guid? LibraryRootId,
    int Page = 1,
    int PageSize = 50,
    string? SortField = null,
    string? SortOrder = "asc",
    string? FileName = null) : IRequest<Result<PagedResult<ScanItemDecisionDto>>>;

// =========================================================================
// Validator
// =========================================================================

public class ListScanDecisionsQueryValidator : AbstractValidator<ListScanDecisionsQuery>
{
    public ListScanDecisionsQueryValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Page must be at least 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100)
            .WithMessage("PageSize must be between 1 and 100.");
    }
}

// =========================================================================
// Handler
// =========================================================================

/// <summary>
///     Returns a paginated list of <see cref="ScanItemDecisionDto" />s for the requested scan run.
///     Joins <c>ScanItemDecision → MediaFile → Media</c> to resolve TMDB title/year/poster,
///     and <c>→ LibraryRoot</c> to resolve the library root path.
///     Parses <c>CandidatesJson</c> into a typed <c>IReadOnlyList&lt;TmdbCandidateDto&gt;</c>.
/// </summary>
public sealed class ListScanDecisionsQueryHandler(IApplicationDbContext db)
    : IRequestHandler<ListScanDecisionsQuery, Result<PagedResult<ScanItemDecisionDto>>>
{
    public async Task<Result<PagedResult<ScanItemDecisionDto>>> Handle(
        ListScanDecisionsQuery request,
        CancellationToken cancellationToken)
    {
        var query = db.ScanItemDecisions
            .AsNoTracking()
            .Where(d => d.ScanRunId == request.ScanRunId);

        // Apply optional filters
        if (request.DecisionType.HasValue)
            query = query.Where(d => d.Kind == request.DecisionType.Value);

        if (request.MediaType.HasValue)
            query = query.Where(d => d.ParsedMediaType == request.MediaType.Value);

        if (request.LibraryRootId.HasValue)
            query = query.Where(d => d.LibraryRootId == request.LibraryRootId.Value);

        if (!string.IsNullOrWhiteSpace(request.FileName))
            query = query.Where(d => d.FilePath.Contains(request.FileName));

        var totalCount = await query.CountAsync(cancellationToken);

        var ordered = (request.SortField?.ToLowerInvariant(), request.SortOrder?.ToLowerInvariant() == "desc") switch
        {
            ("filename", false) => query.OrderBy(d => d.FilePath),
            ("filename", true) => query.OrderByDescending(d => d.FilePath),
            ("status", false) => query.OrderBy(d => d.Kind),
            ("status", true) => query.OrderByDescending(d => d.Kind),
            ("createdat", false) => query.OrderBy(d => d.CreatedAt),
            ("createdat", true) => query.OrderByDescending(d => d.CreatedAt),
            _ => query.OrderBy(d => d.FilePath),
        };

        var items = await ordered
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(d => new
            {
                Decision = d,
                MediaTitle = d.MediaFile != null && d.MediaFile.Media != null
                    ? d.MediaFile.Media.Title
                    : null,
                MediaYear = d.MediaFile != null && d.MediaFile.Media != null
                    ? d.MediaFile.Media.Year ?? (d.MediaFile.Media.ReleaseDate != null
                        ? (int?)d.MediaFile.Media.ReleaseDate.Value.Year
                        : null)
                    : null,
                MediaPosterPath = d.MediaFile != null && d.MediaFile.Media != null
                    ? d.MediaFile.Media.PosterPath
                    : null,
            })
            .ToListAsync(cancellationToken);

        var dtos = items.Select(x => new ScanItemDecisionDto(
            x.Decision.Id,
            x.Decision.ScanRunId,
            x.Decision.FilePath,
            x.Decision.Kind,                 // → decisionType in JSON
            x.Decision.Reason,
            x.Decision.AssignedTmdbId,
            x.Decision.AssignedTmdbKind,     // → assignedKind in JSON
            x.MediaTitle,
            x.MediaYear,
            x.MediaPosterPath,
            ParseCandidates(x.Decision.CandidatesJson),
            x.Decision.ParsedTitle,
            x.Decision.ParsedYear,
            x.Decision.ParsedSeason,
            x.Decision.ParsedEpisode,
            x.Decision.ParsedMediaType,      // → mediaType in JSON
            x.Decision.LibraryRootId,
            x.Decision.MediaFileId,
            x.Decision.CreatedAt             // → decidedAt in JSON
        )).ToList();

        return Result.Success(new PagedResult<ScanItemDecisionDto>(
            dtos,
            totalCount,
            request.Page,
            request.PageSize));
    }

    /// <summary>
    ///     Deserialises the stored <c>CandidatesJson</c> string into a typed list.
    ///     Returns an empty list on any parse failure so the API never crashes on malformed data.
    /// </summary>
    private static IReadOnlyList<TmdbCandidateDto> ParseCandidates(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
            return [];

        try
        {
            var raw = JsonSerializer.Deserialize<List<CandidateJson>>(json);
            return raw?.Select(c => new TmdbCandidateDto(
                    c.TmdbId,
                    Enum.Parse<MediaType>(c.Kind, ignoreCase: true),
                    c.Title,
                    c.Year,
                    c.Score,
                    c.PosterPath))
                .ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    // Private record used only for JSON deserialisation of CandidatesJson
    private record CandidateJson(
        [property: JsonPropertyName("tmdbId")] int TmdbId,
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("year")] int? Year,
        [property: JsonPropertyName("score")] decimal Score,
        [property: JsonPropertyName("posterPath")] string? PosterPath);
}
