using FluentValidation;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Media.DTOs;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

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
    private static readonly Regex SxxExxPattern =
        new(@"(?<![A-Za-z0-9])S(?<season>\d{1,2})E(?<start>\d{1,3})(?:[-E](?<end>\d{1,3}))?(?![A-Za-z0-9])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex XPattern =
        new(@"(?<!\d)(?<season>\d{1,2})[xX](?<start>\d{1,3})(?:-(?<end>\d{1,3}))?(?!\d)",
            RegexOptions.Compiled);

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
        {
            if (request.IsWatched.Value)
                // Watched: must have a UserMedia entry with IsWatched = true
                query = query.Where(m =>
                    m.UserMedias.Any(um => um.UserId == userId.Value && um.IsWatched));
            else
                // Unwatched: no UserMedia entry at all, OR UserMedia with IsWatched = false
                query = query.Where(m =>
                    !m.UserMedias.Any(um => um.UserId == userId.Value && um.IsWatched));
        }

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
                null))
            .ToListAsync(cancellationToken);

        var tvShowIds = items
            .Where(i => i.Type == MediaType.TvShow)
            .Select(i => i.Id)
            .ToList();

        if (tvShowIds.Count > 0)
        {
            var validSeasonRows = await context.TvSeasons
                .AsNoTracking()
                .Where(s => tvShowIds.Contains(s.MediaId)
                            && s.SeasonNumber > 0
                            && !s.Name.ToLower().Contains("specials"))
                .Select(s => new { s.MediaId, s.SeasonNumber })
                .ToListAsync(cancellationToken);

            var linkedSeasonRows = await context.TvSeasons
                .AsNoTracking()
                .Where(s => tvShowIds.Contains(s.MediaId)
                            && s.SeasonNumber > 0
                            && !s.Name.ToLower().Contains("specials")
                            && s.TvEpisodes.Any(e => e.EpisodeFileLinks.Any(l => l.MediaFile.MediaId == s.MediaId)))
                .Select(s => new { s.MediaId, s.SeasonNumber })
                .ToListAsync(cancellationToken);

            var linkedFilePaths = await context.MediaFiles
                .AsNoTracking()
                .Where(f => f.MediaId.HasValue && tvShowIds.Contains(f.MediaId.Value))
                .Select(f => new { MediaId = f.MediaId!.Value, f.FilePath })
                .ToListAsync(cancellationToken);

            var validSeasonsByMedia = validSeasonRows
                .GroupBy(x => x.MediaId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.SeasonNumber).ToHashSet());

            var ownedSeasonsByMedia = linkedSeasonRows
                .GroupBy(x => x.MediaId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.SeasonNumber).ToHashSet());

            var parsedSeasonsByMedia = linkedFilePaths
                .GroupBy(x => x.MediaId)
                .ToDictionary(
                    g => g.Key,
                    g => g
                        .SelectMany(x => ParseOwnedSeasonNumbersFromPath(x.FilePath))
                        .Where(season => season > 0)
                        .ToHashSet());

            var ownedSeasonCountByMedia = new Dictionary<Guid, int>();
            foreach (var mediaId in tvShowIds)
            {
                var owned = ownedSeasonsByMedia.TryGetValue(mediaId, out var linkedOwned)
                    ? new HashSet<int>(linkedOwned)
                    : [];

                if (parsedSeasonsByMedia.TryGetValue(mediaId, out var parsedOwned))
                    owned.UnionWith(parsedOwned);

                if (validSeasonsByMedia.TryGetValue(mediaId, out var validSeasons) && validSeasons.Count > 0)
                    owned.IntersectWith(validSeasons);

                ownedSeasonCountByMedia[mediaId] = owned.Count;
            }

            items = items
                .Select(i => i.Type == MediaType.TvShow
                    ? i with
                    {
                        OwnedSeasonCount = ownedSeasonCountByMedia.TryGetValue(i.Id, out var count)
                            ? count
                            : 0
                    }
                    : i)
                .ToList();
        }

        return Result.Success(new PagedResult<MediaListItemDto>(items, total, request.Page, request.PageSize));
    }

    private static IEnumerable<int> ParseOwnedSeasonNumbersFromPath(string filePath)
        => ParseEpisodeNumbersFromPath(filePath).Select(ep => ep.Season).Distinct();

    private static IEnumerable<(int Season, int Episode)> ParseEpisodeNumbersFromPath(string filePath)
    {
        foreach (var match in SxxExxPattern.Matches(filePath).Cast<Match>())
            foreach (var ep in ExpandMatch(match))
                yield return ep;

        foreach (var match in XPattern.Matches(filePath).Cast<Match>())
            foreach (var ep in ExpandMatch(match))
                yield return ep;
    }

    private static IEnumerable<(int Season, int Episode)> ExpandMatch(Match match)
    {
        var season = ParseGroup(match, "season");
        var start = ParseGroup(match, "start");
        var end = ParseGroup(match, "end") ?? start;

        if (season is null || start is null)
            yield break;

        if (end < start)
            end = start;

        for (var ep = start.Value; ep <= end; ep++)
            yield return (season.Value, ep);
    }

    private static int? ParseGroup(Match match, string groupName)
        => int.TryParse(match.Groups[groupName].Value, out var parsed) ? parsed : null;
}