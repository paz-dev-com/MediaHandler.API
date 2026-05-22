using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Media.Commands.UpdateMediaRootFolder;

public record UpdateMediaRootFolderCommand(Guid MediaId, string? RootFolder) : IRequest<Result<Unit>>;

public class UpdateMediaRootFolderCommandHandler(IApplicationDbContext context)
    : IRequestHandler<UpdateMediaRootFolderCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(UpdateMediaRootFolderCommand request, CancellationToken ct)
    {
        var media = await context.Medias
            .FirstOrDefaultAsync(m => m.Id == request.MediaId, ct);

        if (media is null)
            return Result.Fail<Unit>("NOT_FOUND: Media not found.");

        media.RootFolder = string.IsNullOrWhiteSpace(request.RootFolder)
            ? null
            : request.RootFolder.Trim();

        await context.SaveChangesAsync(ct);
        return Result.Success(Unit.Value);
    }
}

