using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Common.Models.Kodi;
using MediaHandler.Application.Features.KodiImport.DTOs;
using MediaHandler.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.KodiImport.Commands.CreateKodiPathMapping;

public record CreateKodiPathMappingCommand(
    string KodiPrefix,
    string NasPrefix,
    int? SortOrder) : IRequest<Result<KodiPathMappingDto>>;

public sealed class CreateKodiPathMappingCommandHandler(IApplicationDbContext db)
    : IRequestHandler<CreateKodiPathMappingCommand, Result<KodiPathMappingDto>>
{
    public async Task<Result<KodiPathMappingDto>> Handle(
        CreateKodiPathMappingCommand request,
        CancellationToken cancellationToken)
    {
        var kodiPrefix = KodiPathTranslator.NormalizePrefix(request.KodiPrefix);
        var nasPrefix = KodiPathTranslator.NormalizePrefix(request.NasPrefix);

        var duplicate = await db.KodiPathMappings
            .AsNoTracking()
            .AnyAsync(m => m.KodiPrefix == kodiPrefix, cancellationToken);
        if (duplicate)
        {
            return Result.Fail<KodiPathMappingDto>(
                $"DUPLICATE_MAPPING: A mapping for Kodi prefix '{kodiPrefix}' already exists.");
        }

        var sortOrder = request.SortOrder
                        ?? (await db.KodiPathMappings.MaxAsync(m => (int?)m.SortOrder, cancellationToken) ?? -1) + 1;

        var mapping = new KodiPathMapping
        {
            KodiPrefix = kodiPrefix,
            NasPrefix = nasPrefix,
            SortOrder = sortOrder
        };

        db.KodiPathMappings.Add(mapping);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new KodiPathMappingDto(
            mapping.Id, mapping.KodiPrefix, mapping.NasPrefix, mapping.SortOrder));
    }
}
