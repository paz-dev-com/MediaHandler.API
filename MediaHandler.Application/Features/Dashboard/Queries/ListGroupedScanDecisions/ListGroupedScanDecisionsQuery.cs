using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using MediaHandler.Application.Common.DTOs;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Dashboard.DTOs;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Dashboard.Queries.ListGroupedScanDecisions;

/// <summary>
///     Returns scan decisions for a given scan run, grouped by normalized <c>ParsedTitle</c>
///     for TV show episodes. Movie decisions and ungroupable items remain as single-item groups.
/// </summary>
public record ListGroupedScanDecisionsQuery(
    Guid ScanRunId,
    ScanDecisionKind? DecisionType,
    MediaType? MediaType,
    Guid? LibraryRootId) : IRequest<Result<List<ScanDecisionShowGroupDto>>>;

// =========================================================================
// Validator
// =========================================================================

public class ListGroupedScanDecisionsQueryValidator : AbstractValidator<ListGroupedScanDecisionsQuery>
{
    public ListGroupedScanDecisionsQueryValidator()
    {
        RuleFor(x => x.ScanRunId).NotEmpty().WithMessage("ScanRunId is required.");
    }
}

// =========================================================================
// Handler
// =========================================================================

public sealed class ListGroupedScanDecisionsQueryHandler(IApplicationDbContext db)
    : IRequestHandler<ListGroupedScanDecisionsQuery, Result<List<ScanDecisionShowGroupDto>>>
{
    public async Task<Result<List<ScanDecisionShowGroupDto>>> Handle(
        ListGroupedScanDecisionsQuery request,
        CancellationToken cancellationToken)
    {
        var query = db.ScanItemDecisions
            .AsNoTracking()
            .Where(d => d.ScanRunId == request.ScanRunId);

        if (request.DecisionType.HasValue)
            query = query.Where(d => d.Kind == request.DecisionType.Value);

        if (request.MediaType.HasValue)
            query = query.Where(d => d.ParsedMediaType == request.MediaType.Value);

        if (request.LibraryRootId.HasValue)
            query = query.Where(d => d.LibraryRootId == request.LibraryRootId.Value);

        var items = await query
            .OrderBy(d => d.ParsedTitle).ThenBy(d => d.ParsedSeason).ThenBy(d => d.ParsedEpisode)
            .Select(d => new
            {
                Decision = d,
                MediaTitle = d.MediaFile != null && d.MediaFile.Media != null
                    ? d.MediaFile.Media.Title
                    : null,
                MediaYear = d.MediaFile != null && d.MediaFile.Media != null
                    ? d.MediaFile.Media.Year ?? (d.MediaFile.Media.ReleaseDate != null
                        ? (int?)d.MediaFile.Media.ReleaseDate.Value.Year
                        : null)
                    : null,
                MediaPosterPath = d.MediaFile != null && d.MediaFile.Media != null
                    ? d.MediaFile.Media.PosterPath
                    : null,
            })
            .ToListAsync(cancellationToken);

        // Map to DTOs
        var dtos = items.Select(x => new ScanItemDecisionDto(
            x.Decision.Id,
            x.Decision.ScanRunId,
            x.Decision.FilePath,
            x.Decision.Kind,
            x.Decision.Reason,
            x.Decision.AssignedTmdbId,
            x.Decision.AssignedTmdbKind,
            x.MediaTitle,
            x.MediaYear,
            x.MediaPosterPath,
            ParseCandidates(x.Decision.CandidatesJson),
            x.Decision.ParsedTitle,
            x.Decision.ParsedYear,
            x.Decision.ParsedSeason,
            x.Decision.ParsedEpisode,
            x.Decision.ParsedMediaType,
            x.Decision.LibraryRootId,
            x.Decision.MediaFileId,
            x.Decision.CreatedAt
        )).ToList();

        // Deduplicate by FilePath (keep latest DecidedAt)
        var deduped = dtos
            .GroupBy(d => d.FilePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(d => d.DecidedAt).First())
            .ToList();

        // Group TV episodes by normalized ParsedTitle; non-TV items stay as single-item groups
        var groups = new List<ScanDecisionShowGroupDto>();

        var tvItems = deduped.Where(d => d.MediaType == Domain.Enums.MediaType.TvShow && d.ParsedTitle != null).ToList();
        var nonTvItems = deduped.Where(d => d.MediaType != Domain.Enums.MediaType.TvShow || d.ParsedTitle == null).ToList();

        // Group TV by normalized title
        var tvGroups = tvItems
            .GroupBy(d => NormalizeTitle(d.ParsedTitle!), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key);

        foreach (var g in tvGroups)
        {
            var episodes = g.OrderBy(e => e.ParsedSeason).ThenBy(e => e.ParsedEpisode).ToList();

            // Majority TMDB assignment from the group
            var majorityAssignment = episodes
                .Where(e => e.AssignedTmdbId.HasValue)
                .GroupBy(e => e.AssignedTmdbId!.Value)
                .OrderByDescending(ag => ag.Count())
                .FirstOrDefault()
                ?.First();

            // Compute the deterministic GroupId used by AssignTvGroupCommand
            var groupId = TvShowGroup.ComputeGroupId(request.ScanRunId, g.First().ParsedTitle!);

            groups.Add(new ScanDecisionShowGroupDto(
                groupId,
                g.First().ParsedTitle!,
                episodes.Count,
                majorityAssignment?.AssignedTmdbId,
                majorityAssignment?.AssignedKind?.ToString(),
                majorityAssignment?.AssignedTitle,
                majorityAssignment?.AssignedYear,
                majorityAssignment?.AssignedPosterPath,
                episodes));
        }

        // Non-TV items as individual groups
        foreach (var item in nonTvItems.OrderBy(d => d.FilePath))
        {
            groups.Add(new ScanDecisionShowGroupDto(
                null, // GroupId: not applicable for movie/single-item groups
                item.ParsedTitle ?? Path.GetFileNameWithoutExtension(item.FilePath),
                1,
                item.AssignedTmdbId,
                item.AssignedKind?.ToString(),
                item.AssignedTitle,
                item.AssignedYear,
                item.AssignedPosterPath,
                [item]));
        }

        return Result.Success(groups);
    }

    private static string NormalizeTitle(string title)
    {
        // Lowercase, trim, collapse whitespace
        var normalized = title.Trim().ToLowerInvariant();
        // Strip common language suffixes like " (en)", " (fr)", " - VF", " - VOSTFR"
        normalized = System.Text.RegularExpressions.Regex.Replace(
            normalized, @"\s*[\(\-]\s*(en|fr|vf|vostfr|multi|vo)\s*[\)]?\s*$", "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return normalized;
    }

    private static IReadOnlyList<TmdbCandidateDto> ParseCandidates(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
            return [];
        try
        {
            var raw = JsonSerializer.Deserialize<List<CandidateJson>>(json);
            return raw?.Select(c => new TmdbCandidateDto(
                    c.TmdbId,
                    Enum.Parse<Domain.Enums.MediaType>(c.Kind, true),
                    c.Title,
                    c.Year,
                    c.Score,
                    c.PosterPath))
                .ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    private record CandidateJson(
        [property: JsonPropertyName("tmdbId")] int TmdbId,
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("year")] int? Year,
        [property: JsonPropertyName("score")] decimal Score,
        [property: JsonPropertyName("posterPath")] string? PosterPath);
}

