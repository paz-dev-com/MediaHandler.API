using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.KodiImport.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.KodiImport.Queries.GetKodiImportRun;

/// <summary>Returns the detail view of a specific import run.</summary>
public record GetKodiImportRunQuery(Guid Id) : IRequest<Result<ImportRunDetailDto>>;

public sealed class GetKodiImportRunQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetKodiImportRunQuery, Result<ImportRunDetailDto>>
{
    public async Task<Result<ImportRunDetailDto>> Handle(
        GetKodiImportRunQuery request,
        CancellationToken cancellationToken)
    {
        var run = await db.ImportRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);

        if (run is null)
            return Result.Fail<ImportRunDetailDto>($"NOT_FOUND: Import run '{request.Id}' was not found.");

        return Result.Success(ImportRunMappings.ToDetailDto(run));
    }
}
