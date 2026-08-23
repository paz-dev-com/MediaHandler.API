using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.KodiImport.DTOs;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.KodiImport.Queries.GetActiveKodiImport;

/// <summary>
///     Returns the currently running import, or <c>null</c> in <c>Data</c> when none is active.
/// </summary>
public record GetActiveKodiImportQuery : IRequest<Result<ImportRunDto?>>;

public sealed class GetActiveKodiImportQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetActiveKodiImportQuery, Result<ImportRunDto?>>
{
    public async Task<Result<ImportRunDto?>> Handle(
        GetActiveKodiImportQuery request,
        CancellationToken cancellationToken)
    {
        var run = await db.ImportRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Status == ImportRunStatus.Running, cancellationToken);

        return Result.Success(run is null ? null : ImportRunMappings.ToDto(run));
    }
}
