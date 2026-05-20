// ListScanHistory — paginated query for the scan-run history list.

using System.Text.Json;
using FluentValidation;
using MediaHandler.Application.Common.DTOs;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Scan.Queries.ListScanHistory;

/// <summary>
///     Query parameters for listing scan-run history, ordered by <c>StartedAt</c> descending.
/// </summary>
public record ListScanHistoryQuery(
    int Page = 1,
    int PageSize = 20,
    string? SortField = null,
    string? SortOrder = "asc") : IRequest<Result<PagedResult<ScanRunDto>>>;

// =========================================================================
// Validator
// =========================================================================

public class ListScanHistoryQueryValidator : AbstractValidator<ListScanHistoryQuery>
{
    public ListScanHistoryQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0).WithMessage("Page must be at least 1.");
        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100)
            .WithMessage("PageSize must be between 1 and 100.");
    }
}

// =========================================================================
// Handler
// =========================================================================

/// <summary>
///     Returns a paginated list of <see cref="ScanRunDto" />s ordered by <c>StartedAt</c> descending.
/// </summary>
public sealed class ListScanHistoryQueryHandler(IApplicationDbContext db)
    : IRequestHandler<ListScanHistoryQuery, Result<PagedResult<ScanRunDto>>>
{
    public async Task<Result<PagedResult<ScanRunDto>>> Handle(
        ListScanHistoryQuery request,
        CancellationToken cancellationToken)
    {
        var query = db.ScanRuns.AsNoTracking();

        var totalCount = await query.CountAsync(cancellationToken);

        var ordered = (request.SortField?.ToLowerInvariant(), request.SortOrder?.ToLowerInvariant() == "desc") switch
        {
            ("startedat", false) => query.OrderBy(r => r.StartedAt),
            ("startedat", true) => query.OrderByDescending(r => r.StartedAt),
            ("status", false) => query.OrderBy(r => r.Status),
            ("status", true) => query.OrderByDescending(r => r.Status),
            ("mode", false) => query.OrderBy(r => r.Mode),
            ("mode", true) => query.OrderByDescending(r => r.Mode),
            _ => query.OrderByDescending(r => r.StartedAt),
        };

        var items = await ordered
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var dtos = items.Select(run =>
        {
            var rootIds = JsonSerializer.Deserialize<Guid[]>(run.LibraryRootIdsJson) ?? [];
            return new ScanRunDto(
                run.Id,
                run.Mode,
                run.Status,
                run.StartedAt,
                run.FinishedAt,
                run.FailureReason,
                rootIds,
                new ScanCountsDto(
                    run.TotalDiscovered,
                    run.Added,
                    run.Updated,
                    run.Unchanged,
                    run.Removed,
                    run.Excluded,
                    run.NeedsReview));
        }).ToList();

        return Result.Success(new PagedResult<ScanRunDto>(
            dtos,
            totalCount,
            request.Page,
            request.PageSize));
    }
}

