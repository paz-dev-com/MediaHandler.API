using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediatR;

namespace MediaHandler.Application.Features.Tmdb.Commands.ImportFromTmdb;

public record ImportFromTmdbCommand(int TmdbId, string MediaType, string? Language = null) : IRequest<Result<Guid>>;

/// <summary>
///     Handles <see cref="ImportFromTmdbCommand" /> by delegating to <see cref="IMediaImportService" />
///     which encapsulates the deduplication check, TMDB fetch, and entity persistence.
/// </summary>
public class ImportFromTmdbCommandHandler(IMediaImportService importService)
    : IRequestHandler<ImportFromTmdbCommand, Result<Guid>>
{
    public Task<Result<Guid>> Handle(ImportFromTmdbCommand request, CancellationToken cancellationToken)
    {
        return importService.ImportOrGetExistingAsync(request.TmdbId, request.MediaType, request.Language,
            cancellationToken);
    }
}