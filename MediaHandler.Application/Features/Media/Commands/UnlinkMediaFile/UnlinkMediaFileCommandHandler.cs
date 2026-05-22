using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Media.Commands.UnlinkMediaFile;

public record UnlinkMediaFileCommand(Guid MediaId, Guid FileId) : IRequest<Result<Unit>>;

public class UnlinkMediaFileCommandHandler(IApplicationDbContext context)
    : IRequestHandler<UnlinkMediaFileCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(UnlinkMediaFileCommand command, CancellationToken cancellationToken)
    {
        var mediaFile = await context.MediaFiles
            .FirstOrDefaultAsync(f => f.Id == command.FileId, cancellationToken);

        if (mediaFile is null)
            return Result.Fail<Unit>("NOT_FOUND: MediaFile not found.");

        if (mediaFile.MediaId != command.MediaId)
            return Result.Fail<Unit>("NOT_FOUND: file not linked to this media item.");

        mediaFile.MediaId = null;
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success(Unit.Value);
    }
}

