using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Media.Commands.LinkMediaFile;

public record LinkMediaFileCommand(Guid MediaId, Guid FileId) : IRequest<Result<Unit>>;

public class LinkMediaFileCommandHandler(IApplicationDbContext context)
    : IRequestHandler<LinkMediaFileCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(LinkMediaFileCommand command, CancellationToken cancellationToken)
    {
        var mediaFile = await context.MediaFiles
            .FirstOrDefaultAsync(f => f.Id == command.FileId, cancellationToken);

        if (mediaFile is null)
            return Result.Fail<Unit>("NOT_FOUND: MediaFile not found.");

        var mediaExists = await context.Medias
            .AnyAsync(m => m.Id == command.MediaId, cancellationToken);

        if (!mediaExists)
            return Result.Fail<Unit>("NOT_FOUND: Media not found.");

        if (mediaFile.MediaId == command.MediaId)
            return Result.Success(Unit.Value);

        if (mediaFile.MediaId is not null)
            return Result.Fail<Unit>($"FILE_ALREADY_LINKED: File is already linked to media '{mediaFile.MediaId}'.");

        mediaFile.MediaId = command.MediaId;
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success(Unit.Value);
    }
}

