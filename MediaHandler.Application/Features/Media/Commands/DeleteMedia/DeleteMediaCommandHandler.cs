using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Media.Commands.DeleteMedia;

public record DeleteMediaCommand(Guid Id) : IRequest<Result>;

public class DeleteMediaCommandHandler(IApplicationDbContext context)
    : IRequestHandler<DeleteMediaCommand, Result>
{
    public async Task<Result> Handle(DeleteMediaCommand request, CancellationToken cancellationToken)
    {
        var media = await context.Medias
            .FirstOrDefaultAsync(m => m.Id == request.Id, cancellationToken);

        if (media is null)
            return Result.Fail("Media not found.");

        context.Medias.Remove(media);
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}