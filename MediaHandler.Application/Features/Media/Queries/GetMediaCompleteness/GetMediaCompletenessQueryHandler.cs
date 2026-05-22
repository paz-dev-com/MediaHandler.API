using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Media.DTOs;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace MediaHandler.Application.Features.Media.Queries.GetMediaCompleteness;

public record GetMediaCompletenessQuery(Guid MediaId) : IRequest<Result<IReadOnlyList<SeasonCompletenessDto>>>;

public class GetMediaCompletenessQueryHandler(IApplicationDbContext context)
    : IRequestHandler<GetMediaCompletenessQuery, Result<IReadOnlyList<SeasonCompletenessDto>>>
{
    private static readonly Regex SxxExxPattern =
        new(@"(?<![A-Za-z0-9])S(?<season>\d{1,2})E(?<start>\d{1,3})(?:[-E](?<end>\d{1,3}))?(?![A-Za-z0-9])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex XPattern =
        new(@"(?<!\d)(?<season>\d{1,2})[xX](?<start>\d{1,3})(?:-(?<end>\d{1,3}))?(?!\d)",
            RegexOptions.Compiled);

    public async Task<Result<IReadOnlyList<SeasonCompletenessDto>>> Handle(
        GetMediaCompletenessQuery request,
        CancellationToken ct)
    {
        var media = await context.Medias
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == request.MediaId, ct);

        if (media is null)
            return Result.Fail<IReadOnlyList<SeasonCompletenessDto>>("NOT_FOUND: Media not found.");

        if (media.Type != MediaType.TvShow)
            return Result.Fail<IReadOnlyList<SeasonCompletenessDto>>(
                "MEDIA_NOT_TV_SHOW: Completeness is only supported for TV shows.");

        var seasons = await context.TvSeasons
            .AsNoTracking()
            .Where(s => s.MediaId == request.MediaId
                        && s.SeasonNumber != 0
                        && !s.Name.ToLower().Contains("specials"))
            .Include(s => s.TvEpisodes)
                .ThenInclude(e => e.EpisodeFileLinks)
                    .ThenInclude(l => l.MediaFile)
            .OrderBy(s => s.SeasonNumber)
            .ToListAsync(ct);

        var linkedFilePaths = await context.MediaFiles
            .AsNoTracking()
            .Where(f => f.MediaId == request.MediaId)
            .Select(f => f.FilePath)
            .ToListAsync(ct);

        var parsedOwnedEpisodesBySeason = linkedFilePaths
            .SelectMany(ParseEpisodeNumbersFromPath)
            .Where(ep => ep.Season > 0 && ep.Episode > 0)
            .GroupBy(ep => ep.Season)
            .ToDictionary(g => g.Key, g => g.Select(ep => ep.Episode).ToHashSet());

        var dtos = seasons.Select(season =>
        {
            var totalExpected = season.EpisodeCount ?? season.TvEpisodes.Count;
            var owned = season.TvEpisodes
                .Where(e => e.EpisodeFileLinks.Any(l => l.MediaFile.MediaId == request.MediaId))
                .Select(e => e.EpisodeNumber)
                .ToHashSet();

            if (parsedOwnedEpisodesBySeason.TryGetValue(season.SeasonNumber, out var parsedOwnedEpisodes))
                owned.UnionWith(parsedOwnedEpisodes);

            var missing = Enumerable.Range(1, totalExpected)
                .Except(owned)
                .OrderBy(n => n)
                .ToList();

            return new SeasonCompletenessDto(
                season.SeasonNumber,
                season.Name,
                totalExpected,
                owned.Count,
                missing.AsReadOnly(),
                missing.Count == 0);
        }).ToList();

        return Result.Success<IReadOnlyList<SeasonCompletenessDto>>(dtos.AsReadOnly());
    }

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

