using FluentValidation;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Media.DTOs;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Media.Queries.GetMediaList;

public record GetMediaListQuery(
    int Page = 1,
    int PageSize = 20,
    string? Search = null,
    MediaType? Type = null,
    bool? IsWatched = null,
    string? Genre = null) : IRequest<Result<PagedResult<MediaListItemDto>>>;

public class GetMediaListQueryValidator : AbstractValidator<GetMediaListQuery>
{
    public GetMediaListQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public class GetMediaListQueryHandler(IApplicationDbContext context, ICurrentUserService currentUser)
    : IRequestHandler<GetMediaListQuery, Result<PagedResult<MediaListItemDto>>>
{
    public async Task<Result<PagedResult<MediaListItemDto>>> Handle(GetMediaListQuery request,
        CancellationToken cancellationToken)
    {
        var oktaId = currentUser.OktaId;
        Guid? userId = null;
        if (oktaId is not null)
            userId = await context.Users
                .AsNoTracking()
                .Where(u => u.OktaId == oktaId)
                .Select(u => (Guid?)u.Id)
                .FirstOrDefaultAsync(cancellationToken);

        var query = context.Medias.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(m =>
                m.Title.Contains(request.Search) ||
                (m.OriginalTitle != null && m.OriginalTitle.Contains(request.Search)));

        if (request.Type.HasValue)
            query = query.Where(m => m.Type == request.Type.Value);

        if (request.IsWatched.HasValue && userId.HasValue)
            query = query.Where(m =>
                m.UserMedias.Any(um => um.UserId == userId.Value && um.IsWatched == request.IsWatched.Value));

        if (!string.IsNullOrWhiteSpace(request.Genre))
            query = query.Where(m => m.Genres.Any(g => g.Name == request.Genre));

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(m => m.Title)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(m => new MediaListItemDto(
                m.Id,
                m.TmdbId,
                m.Title,
                m.Type,
                m.ReleaseDate,
                m.PosterPath,
                m.VoteAverage,
                m.MediaFiles.Count,
                userId.HasValue
                    ? m.UserMedias.Where(um => um.UserId == userId.Value).Select(um => (bool?)um.IsWatched)
                        .FirstOrDefault()
                    : null,
                m.Status,
                m.NumberOfSeasons,
                m.Type == MediaType.TvShow ? (int?)m.TvSeasons.Count() : null))
            .ToListAsync(cancellationToken);

        return Result.Success(new PagedResult<MediaListItemDto>(items, total, request.Page, request.PageSize));
    }
}