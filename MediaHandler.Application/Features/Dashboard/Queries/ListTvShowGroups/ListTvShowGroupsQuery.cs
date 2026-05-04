// ListTvShowGroups — query, validator, and handler that computes TV show episode groupings
// on-the-fly from ScanItemDecision rows for a given scan run.
// Groups decisions by ParsedTitle where ParsedMediaType = TvShow, computes a deterministic
// GroupId via TvShowGroup.ComputeGroupId, and resolves TMDB assignment from the Media table.

using FluentValidation;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Dashboard.DTOs;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Dashboard.Queries.ListTvShowGroups;

/// <summary>Query to list on-the-fly TV show groups for a given scan run.</summary>
public record ListTvShowGroupsQuery(Guid ScanId) : IRequest<Result<List<TvShowGroupDto>>>;

// =========================================================================
// Validator
// =========================================================================

public class ListTvShowGroupsQueryValidator : AbstractValidator<ListTvShowGroupsQuery>
{
    public ListTvShowGroupsQueryValidator()
    {
        RuleFor(x => x.ScanId)
            .NotEmpty().WithMessage("ScanId is required.");
    }
}

// =========================================================================
// Handler
// =========================================================================

/// <summary>
///     Handles <see cref="ListTvShowGroupsQuery" />.
///     <list type="bullet">
///         <item>Materialises TV decisions for the scan (client-side grouping avoids EF GroupBy nav-property limits).</item>
///         <item>Groups by <c>ParsedTitle</c> (the show name stored on TV decisions).</item>
///         <item>Computes a deterministic <c>GroupId</c> via <see cref="TvShowGroup.ComputeGroupId" />.</item>
///         <item>Resolves TMDB title/year/poster from the <c>Media</c> table.</item>
///     </list>
/// </summary>
public sealed class ListTvShowGroupsQueryHandler(IApplicationDbContext db)
    : IRequestHandler<ListTvShowGroupsQuery, Result<List<TvShowGroupDto>>>
{
    public async Task<Result<List<TvShowGroupDto>>> Handle(
        ListTvShowGroupsQuery request,
        CancellationToken cancellationToken)
    {
        // Materialise TV decisions for this scan.
        // ParsedTitle on TV decisions stores the SHOW name (not episode title).
        var tvDecisions = await db.ScanItemDecisions
            .AsNoTracking()
            .Where(d => d.ScanRunId == request.ScanId
                        && d.ParsedMediaType == MediaType.TvShow
                        && d.ParsedTitle != null)
            .Select(d => new
            {
                d.Id,
                d.ParsedTitle,
                d.AssignedTmdbId,
                d.AssignedTmdbKind
            })
            .ToListAsync(cancellationToken);

        // Group by show name (case-insensitive) and compute deterministic GroupId per group.
        var rawGroups = tvDecisions
            .GroupBy(d => d.ParsedTitle!, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var assignedEntry = g.FirstOrDefault(d => d.AssignedTmdbId.HasValue);
                return new
                {
                    ParsedShowName = g.Key,
                    GroupId = TvShowGroup.ComputeGroupId(request.ScanId, g.Key),
                    Count = g.Count(),
                    AssignedTmdbId = assignedEntry?.AssignedTmdbId,
                    AssignedTmdbKind = assignedEntry?.AssignedTmdbKind
                };
            })
            .ToList();

        // Load Media rows for all assigned TMDB IDs to resolve title / year / poster.
        var tmdbIds = rawGroups
            .Where(g => g.AssignedTmdbId.HasValue)
            .Select(g => g.AssignedTmdbId!.Value)
            .Distinct()
            .ToList();

        var mediaLookup = new Dictionary<int, (string Title, int? Year, string? PosterPath)>();
        if (tmdbIds.Count > 0)
        {
            var mediaRows = await db.Medias
                .AsNoTracking()
                .Where(m => tmdbIds.Contains(m.TmdbId))
                .Select(m => new { m.TmdbId, m.Title, m.Year, m.PosterPath })
                .ToListAsync(cancellationToken);

            foreach (var m in mediaRows)
                mediaLookup[m.TmdbId] = (m.Title, m.Year, m.PosterPath);
        }

        // Map to DTOs.
        var dtos = rawGroups.Select(g =>
        {
            mediaLookup.TryGetValue(g.AssignedTmdbId ?? 0, out var media);
            return new TvShowGroupDto(
                g.GroupId,
                g.ParsedShowName,
                g.Count,
                g.AssignedTmdbId,
                g.AssignedTmdbKind,
                g.AssignedTmdbId.HasValue ? media.Title : null,
                g.AssignedTmdbId.HasValue ? media.Year : null,
                g.AssignedTmdbId.HasValue ? media.PosterPath : null);
        }).ToList();

        return Result.Success(dtos);
    }
}

