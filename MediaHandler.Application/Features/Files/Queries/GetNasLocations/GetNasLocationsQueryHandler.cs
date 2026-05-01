using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediatR;

namespace MediaHandler.Application.Features.Files.Queries.GetNasLocations;

public record GetNasLocationsQuery : IRequest<Result<IReadOnlyList<string>>>;

public class GetNasLocationsQueryHandler(INasService nas)
    : IRequestHandler<GetNasLocationsQuery, Result<IReadOnlyList<string>>>
{
    public async Task<Result<IReadOnlyList<string>>> Handle(
        GetNasLocationsQuery request,
        CancellationToken cancellationToken)
    {
        var paths = await nas.GetConfiguredPathsAsync(cancellationToken);
        return Result.Success(paths);
    }
}