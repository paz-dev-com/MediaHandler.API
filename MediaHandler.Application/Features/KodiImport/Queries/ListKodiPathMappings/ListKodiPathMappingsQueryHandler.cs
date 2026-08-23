using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.KodiImport.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.KodiImport.Queries.ListKodiPathMappings;

/// <summary>Lists all persisted Kodi path mappings in evaluation order.</summary>
public record ListKodiPathMappingsQuery : IRequest<Result<IReadOnlyList<KodiPathMappingDto>>>;

public sealed class ListKodiPathMappingsQueryHandler(IApplicationDbContext db)
    : IRequestHandler<ListKodiPathMappingsQuery, Result<IReadOnlyList<KodiPathMappingDto>>>
{
    public async Task<Result<IReadOnlyList<KodiPathMappingDto>>> Handle(
        ListKodiPathMappingsQuery request,
        CancellationToken cancellationToken)
    {
        var mappings = await db.KodiPathMappings
            .AsNoTracking()
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken);

        IReadOnlyList<KodiPathMappingDto> dtos = mappings
            .Select(m => new KodiPathMappingDto(m.Id, m.KodiPrefix, m.NasPrefix, m.SortOrder))
            .ToList();

        return Result.Success(dtos);
    }
}
