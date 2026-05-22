using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Media.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Media.Queries.GetUnlinkedFiles;

public record GetUnlinkedFilesQuery(int Page, int PageSize) : IRequest<Result<PagedResult<UnlinkedFileDto>>>;

public class GetUnlinkedFilesQueryHandler(IApplicationDbContext context)
    : IRequestHandler<GetUnlinkedFilesQuery, Result<PagedResult<UnlinkedFileDto>>>
{
    public async Task<Result<PagedResult<UnlinkedFileDto>>> Handle(
        GetUnlinkedFilesQuery request,
        CancellationToken cancellationToken)
    {
        var query = context.MediaFiles
            .AsNoTracking()
            .Where(f => f.MediaId == null)
            .OrderBy(f => f.FilePath);

        var count = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(f => new UnlinkedFileDto(f.Id, f.FilePath, f.FileSizeBytes, f.Format, f.Resolution))
            .ToListAsync(cancellationToken);

        return Result.Success(new PagedResult<UnlinkedFileDto>(items, count, request.Page, request.PageSize));
    }
}

