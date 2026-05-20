using System.Text.Json;
using FluentValidation;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Dashboard.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Dashboard.Queries.ListEnrichmentHistory;

/// <summary>
///     Returns a paginated list of past enrichment runs, ordered by most recent first.
/// </summary>
public record ListEnrichmentHistoryQuery(
    int Page,
    int PageSize,
    string? SortField = null,
    string? SortOrder = "asc") : IRequest<PagedResult<EnrichmentRunDto>>;

// =========================================================================
// Validator
// =========================================================================

public class ListEnrichmentHistoryQueryValidator : AbstractValidator<ListEnrichmentHistoryQuery>
{
    public ListEnrichmentHistoryQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1).WithMessage("page must be ≥ 1.");
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100).WithMessage("pageSize must be between 1 and 100.");
    }
}

// =========================================================================
// Handler
// =========================================================================

public sealed class ListEnrichmentHistoryQueryHandler(IApplicationDbContext db)
    : IRequestHandler<ListEnrichmentHistoryQuery, PagedResult<EnrichmentRunDto>>
{
    public async Task<PagedResult<EnrichmentRunDto>> Handle(
        ListEnrichmentHistoryQuery request,
        CancellationToken cancellationToken)
    {
        var query = db.EnrichmentRuns.AsNoTracking();

        var totalCount = await query.CountAsync(cancellationToken);

        var ordered = (request.SortField?.ToLowerInvariant(), request.SortOrder?.ToLowerInvariant() == "desc") switch
        {
            ("startedat", false) => query.OrderBy(r => r.StartedAt),
            ("startedat", true) => query.OrderByDescending(r => r.StartedAt),
            ("status", false) => query.OrderBy(r => r.Status),
            ("status", true) => query.OrderByDescending(r => r.Status),
            _ => query.OrderByDescending(r => r.StartedAt),
        };

        var items = await ordered
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var dtos = items.Select(r =>
        {
            IReadOnlyList<EnrichmentErrorDetailDto> errors = [];
            try
            {
                if (!string.IsNullOrWhiteSpace(r.ErrorDetailsJson) && r.ErrorDetailsJson != "[]")
                    errors = JsonSerializer.Deserialize<List<EnrichmentErrorDetailDto>>(r.ErrorDetailsJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            }
            catch
            {
                /* ignore malformed JSON */
            }

            return new EnrichmentRunDto(
                r.Id,
                r.Status,
                r.StartedAt,
                r.FinishedAt,
                r.TotalItems,
                r.EnrichedCount,
                r.FailedCount,
                r.SkippedCount,
                r.CurrentItem,
                errors);
        }).ToList();

        return new PagedResult<EnrichmentRunDto>(
            dtos,
            request.Page,
            request.PageSize,
            totalCount);
    }
}

