using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.KodiImport.Commands.DeleteKodiPathMapping;

public record DeleteKodiPathMappingCommand(Guid Id) : IRequest<Result<Unit>>;

public sealed class DeleteKodiPathMappingCommandHandler(IApplicationDbContext db)
    : IRequestHandler<DeleteKodiPathMappingCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(
        DeleteKodiPathMappingCommand request,
        CancellationToken cancellationToken)
    {
        var mapping = await db.KodiPathMappings
            .FirstOrDefaultAsync(m => m.Id == request.Id, cancellationToken);

        if (mapping is null)
            return Result.Fail<Unit>(
                $"NOT_FOUND: Kodi path mapping '{request.Id}' was not found.");

        db.KodiPathMappings.Remove(mapping);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(Unit.Value);
    }
}
