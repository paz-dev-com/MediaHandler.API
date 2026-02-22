using MediaHandler.Application.Common.Models;
using MediaHandler.Domain.Interfaces;
using MediatR;

namespace MediaHandler.Application.Features.Tmdb.Queries.SearchTmdb;

public record SearchTmdbQuery(string Query, string? Language = null) : IRequest<Result<IReadOnlyList<TmdbMediaDto>>>;

public class SearchTmdbQueryHandler : IRequestHandler<SearchTmdbQuery, Result<IReadOnlyList<TmdbMediaDto>>>
{
    private readonly ITmdbService _tmdb;
    private readonly ICurrentUserService _currentUser;

    public SearchTmdbQueryHandler(ITmdbService tmdb, ICurrentUserService currentUser)
    {
        _tmdb = tmdb;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<TmdbMediaDto>>> Handle(SearchTmdbQuery request, CancellationToken cancellationToken)
    {
        var language = request.Language ?? "en";
        var result = await _tmdb.SearchMediaAsync(request.Query, language, cancellationToken);

        return result is null
            ? Result.Success<IReadOnlyList<TmdbMediaDto>>(Array.Empty<TmdbMediaDto>())
            : Result.Success<IReadOnlyList<TmdbMediaDto>>(new[] { result });
    }
}
