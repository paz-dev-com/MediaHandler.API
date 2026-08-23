using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Common.Models.Kodi;
using MediaHandler.Application.Features.KodiImport.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.KodiImport.Commands.UpdateKodiPathMapping;

public record UpdateKodiPathMappingCommand(
    Guid Id,
    string KodiPrefix,
    string NasPrefix,
    int SortOrder) : IRequest<Result<KodiPathMappingDto>>;

public sealed class UpdateKodiPathMappingCommandHandler(IApplicationDbContext db)
    : IRequestHandler<UpdateKodiPathMappingCommand, Result<KodiPathMappingDto>>
{
    public async Task<Result<KodiPathMappingDto>> Handle(
        UpdateKodiPathMappingCommand request,
        CancellationToken cancellationToken)
    {
        var mapping = await db.KodiPathMappings
            .FirstOrDefaultAsync(m => m.Id == request.Id, cancellationToken);

        if (mapping is null)
            return Result.Fail<KodiPathMappingDto>(
                $"NOT_FOUND: Kodi path mapping '{request.Id}' was not found.");

        var kodiPrefix = KodiPathTranslator.NormalizePrefix(request.KodiPrefix);
        var nasPrefix = KodiPathTranslator.NormalizePrefix(request.NasPrefix);

        var duplicate = await db.KodiPathMappings
            .AsNoTracking()
            .AnyAsync(m => m.KodiPrefix == kodiPrefix && m.Id != request.Id, cancellationToken);
        if (duplicate)
        {
            return Result.Fail<KodiPathMappingDto>(
                $"DUPLICATE_MAPPING: A mapping for Kodi prefix '{kodiPrefix}' already exists.");
        }

        mapping.KodiPrefix = kodiPrefix;
        mapping.NasPrefix = nasPrefix;
        mapping.SortOrder = request.SortOrder;

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new KodiPathMappingDto(
            mapping.Id, mapping.KodiPrefix, mapping.NasPrefix, mapping.SortOrder));
    }
}
