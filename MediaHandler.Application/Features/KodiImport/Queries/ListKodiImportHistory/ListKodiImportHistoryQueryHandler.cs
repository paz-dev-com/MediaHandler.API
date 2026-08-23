using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.KodiImport.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.KodiImport.Queries.ListKodiImportHistory;

/// <summary>
///     Paginated import-run history, ordered by <c>StartedAt</c> descending (newest first).
/// </summary>
public record ListKodiImportHistoryQuery(int Page = 1, int PageSize = 20)
    : IRequest<Result<PagedResult<ImportRunDto>>>;

public sealed class ListKodiImportHistoryQueryHandler(IApplicationDbContext db)
    : IRequestHandler<ListKodiImportHistoryQuery, Result<PagedResult<ImportRunDto>>>
{
    public async Task<Result<PagedResult<ImportRunDto>>> Handle(
        ListKodiImportHistoryQuery request,
        CancellationToken cancellationToken)
    {
        var query = db.ImportRuns.AsNoTracking();

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(r => r.StartedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var dtos = items.Select(ImportRunMappings.ToDto).ToList();

        return Result.Success(new PagedResult<ImportRunDto>(dtos, totalCount, request.Page, request.PageSize));
    }
}
