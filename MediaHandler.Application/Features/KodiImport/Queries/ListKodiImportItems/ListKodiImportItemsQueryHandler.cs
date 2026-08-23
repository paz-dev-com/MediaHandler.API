using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.KodiImport.DTOs;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.KodiImport.Queries.ListKodiImportItems;

/// <summary>
///     Paginated per-item outcome list of an import run, with optional outcome/kind filters.
/// </summary>
public record ListKodiImportItemsQuery(
    Guid RunId,
    ImportItemStatus? Outcome,
    KodiItemKind? Kind,
    int Page = 1,
    int PageSize = 50) : IRequest<Result<PagedResult<ImportItemOutcomeDto>>>;

public sealed class ListKodiImportItemsQueryHandler(IApplicationDbContext db)
    : IRequestHandler<ListKodiImportItemsQuery, Result<PagedResult<ImportItemOutcomeDto>>>
{
    public async Task<Result<PagedResult<ImportItemOutcomeDto>>> Handle(
        ListKodiImportItemsQuery request,
        CancellationToken cancellationToken)
    {
        var runExists = await db.ImportRuns
            .AsNoTracking()
            .AnyAsync(r => r.Id == request.RunId, cancellationToken);

        if (!runExists)
            return Result.Fail<PagedResult<ImportItemOutcomeDto>>(
                $"NOT_FOUND: Import run '{request.RunId}' was not found.");

        var query = db.ImportItemOutcomes
            .AsNoTracking()
            .Where(o => o.ImportRunId == request.RunId);

        if (request.Outcome.HasValue)
            query = query.Where(o => o.Outcome == request.Outcome.Value);

        if (request.Kind.HasValue)
            query = query.Where(o => o.KodiItemKind == request.Kind.Value);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(o => o.CreatedAt)
            .ThenBy(o => o.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var dtos = items.Select(o => new ImportItemOutcomeDto(
            o.Id,
            o.KodiItemKind,
            o.KodiItemId,
            o.Title,
            o.MediaKind,
            o.Outcome,
            o.LinkOutcome,
            o.LinkedFileCount,
            o.Reason,
            o.KodiPathPrefix,
            o.MediaId)).ToList();

        return Result.Success(new PagedResult<ImportItemOutcomeDto>(dtos, totalCount, request.Page, request.PageSize));
    }
}
