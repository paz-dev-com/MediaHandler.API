using MediaHandler.Application.Common.DTOs;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediatR;

namespace MediaHandler.Application.Features.Tmdb.Queries.SearchTmdb;

public record SearchTmdbQuery(string Query, string? Language = null) : IRequest<Result<IReadOnlyList<TmdbMediaDto>>>;

public class SearchTmdbQueryHandler(ITmdbService tmdb)
    : IRequestHandler<SearchTmdbQuery, Result<IReadOnlyList<TmdbMediaDto>>>
{
    public async Task<Result<IReadOnlyList<TmdbMediaDto>>> Handle(SearchTmdbQuery request,
        CancellationToken cancellationToken)
    {
        var result = await tmdb.SearchMediaAsync(request.Query, request.Language ?? "en", cancellationToken);

        return result is null
            ? Result.Success<IReadOnlyList<TmdbMediaDto>>(Array.Empty<TmdbMediaDto>())
            : Result.Success<IReadOnlyList<TmdbMediaDto>>(new[] { result });
    }
}