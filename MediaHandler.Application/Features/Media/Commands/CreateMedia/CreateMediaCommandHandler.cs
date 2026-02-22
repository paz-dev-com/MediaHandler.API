using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Domain.Enums;
using MediatR;

namespace MediaHandler.Application.Features.Media.Commands.CreateMedia;

public record CreateMediaCommand(
    int TmdbId,
    string Title,
    string? OriginalTitle,
    string? Overview,
    MediaType Type,
    DateTime? ReleaseDate,
    int? Runtime,
    string? PosterPath,
    string? BackdropPath,
    decimal? VoteAverage,
    int? VoteCount,
    string? Genres,
    string? Language) : IRequest<Result<Guid>>;

public class CreateMediaCommandHandler : IRequestHandler<CreateMediaCommand, Result<Guid>>
{
    private readonly IApplicationDbContext _context;

    public CreateMediaCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<Guid>> Handle(CreateMediaCommand request, CancellationToken cancellationToken)
    {
        var media = new Domain.Entities.Media
        {
            TmdbId = request.TmdbId,
            Title = request.Title,
            OriginalTitle = request.OriginalTitle,
            Overview = request.Overview,
            Type = request.Type,
            ReleaseDate = request.ReleaseDate,
            Runtime = request.Runtime,
            PosterPath = request.PosterPath,
            BackdropPath = request.BackdropPath,
            VoteAverage = request.VoteAverage,
            VoteCount = request.VoteCount,
            Genres = request.Genres,
            Language = request.Language
        };

        _context.Medias.Add(media);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(media.Id);
    }
}
