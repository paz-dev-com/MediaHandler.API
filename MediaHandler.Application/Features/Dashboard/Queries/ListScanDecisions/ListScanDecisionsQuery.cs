// ListScanDecisions — paginated query for the scan decisions browser.
// Returns all ScanItemDecision rows for a given ScanRun with optional filters,
// joining MediaFile → Media to populate assignedTitle/Year/PosterPath,
// and LibraryRoot to populate libraryRootPath.

using FluentValidation;
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
    int PageSize = 50) : IRequest<Result<PagedResult<ScanItemDecisionDto>>>;

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

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(d => d.FilePath)
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
                LibraryRootPath = d.LibraryRoot != null ? d.LibraryRoot.Path : null
            })
            .ToListAsync(cancellationToken);

        var dtos = items.Select(x => new ScanItemDecisionDto(
            x.Decision.Id,
            x.Decision.ScanRunId,
            x.Decision.FilePath,
            x.Decision.Kind,
            x.Decision.Reason,
            x.Decision.AssignedTmdbId,
            x.Decision.AssignedTmdbKind,
            x.MediaTitle,
            x.MediaYear,
            x.MediaPosterPath,
            x.Decision.CandidatesJson,
            x.Decision.ParsedTitle,
            x.Decision.ParsedYear,
            x.Decision.ParsedSeason,
            x.Decision.ParsedEpisode,
            x.Decision.ParsedMediaType,
            x.Decision.LibraryRootId,
            x.LibraryRootPath,
            x.Decision.MediaFileId
        )).ToList();

        return Result.Success(new PagedResult<ScanItemDecisionDto>(
            dtos,
            totalCount,
            request.Page,
            request.PageSize));
    }
}

