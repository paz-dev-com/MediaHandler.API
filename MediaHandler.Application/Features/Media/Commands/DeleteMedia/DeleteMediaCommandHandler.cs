using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Media.Commands.DeleteMedia;

public record DeleteMediaCommand(Guid Id) : IRequest<Result>;

public class DeleteMediaCommandHandler : IRequestHandler<DeleteMediaCommand, Result>
{
    private readonly IApplicationDbContext _context;

    public DeleteMediaCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result> Handle(DeleteMediaCommand request, CancellationToken cancellationToken)
    {
        var media = await _context.Medias
            .FirstOrDefaultAsync(m => m.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Domain.Entities.Media), request.Id);

        _context.Medias.Remove(media);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
