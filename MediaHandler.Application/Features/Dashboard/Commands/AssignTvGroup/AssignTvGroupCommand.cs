// AssignTvGroup — command, validator, and handler for assigning a TMDB TV show to an entire
// TV show group (all ScanItemDecisions sharing the same ParsedTitle within a scan run).
// Resolves group members by recomputing the deterministic GroupId for every ParsedTitle group,
// verifies the TMDB ID via ITmdbService, bulk-updates AssignedTmdbId/Kind on all member
// decisions, and updates the linked MediaFile.MediaId for each.
// T058 fix: tracks old MediaIds and removes orphaned Media rows when no files remain.

using FluentValidation;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Dashboard.Commands.AssignTvGroup;

/// <summary>Application-layer result returned by <see cref="AssignTvGroupCommandHandler" />.</summary>
public record AssignTvGroupResult(
    Guid GroupId,
    string ParsedShowName,
    int EpisodeCount,
    int? AssignedTmdbId,
    MediaType? AssignedTmdbKind,
    string? AssignedTitle,
    int? AssignedYear,
    string? AssignedPosterPath);

/// <summary>Command to assign a TMDB TV show to all episode decisions in a TV show group.</summary>
public record AssignTvGroupCommand(
    Guid GroupId,
    Guid ScanId,
    int TmdbId) : IRequest<Result<AssignTvGroupResult>>;

// =========================================================================
// Validator
// =========================================================================

public class AssignTvGroupCommandValidator : AbstractValidator<AssignTvGroupCommand>
{
    public AssignTvGroupCommandValidator()
    {
        RuleFor(x => x.GroupId)
            .NotEmpty().WithMessage("GroupId is required.");

        RuleFor(x => x.ScanId)
            .NotEmpty().WithMessage("ScanId is required.");

        RuleFor(x => x.TmdbId)
            .GreaterThan(0).WithMessage("TmdbId must be a positive integer.");
    }
}

// =========================================================================
// Handler
// =========================================================================

/// <summary>
///     Handles <see cref="AssignTvGroupCommand" />.
///     <list type="bullet">
///         <item>Loads all TV decisions for the scan run.</item>
///         <item>Resolves group members by recomputing <c>GroupId = SHA256(scanId|parsedTitle.ToLower())</c>.</item>
///         <item>Returns failure if no decisions match the requested <paramref name="GroupId" />.</item>
///         <item>Verifies the TMDB id via <see cref="ITmdbService" />.</item>
///         <item>Looks up an existing <c>Media</c> row for the new TMDB ID, or creates one.</item>
///         <item>Updates <c>AssignedTmdbId</c>/<c>AssignedTmdbKind</c> on all decisions.</item>
///         <item>Updates <c>MediaFile.MediaId</c> for every linked media file.</item>
///         <item>Removes old <c>Media</c> rows that become orphaned (no remaining <c>MediaFile</c> references).</item>
///         <item>Saves all changes atomically.</item>
///     </list>
/// </summary>
public sealed class AssignTvGroupCommandHandler(
    IApplicationDbContext db,
    ITmdbService tmdbService)
    : IRequestHandler<AssignTvGroupCommand, Result<AssignTvGroupResult>>
{
    public async Task<Result<AssignTvGroupResult>> Handle(
        AssignTvGroupCommand request,
        CancellationToken cancellationToken)
    {
        // Load all TV decisions for the scan (with their MediaFiles)
        var allTvDecisions = await db.ScanItemDecisions
            .Include(d => d.MediaFile)
            .Where(d => d.ScanRunId == request.ScanId
                        && d.ParsedMediaType == MediaType.TvShow
                        && d.ParsedTitle != null)
            .ToListAsync(cancellationToken);

        // Resolve group members by recomputing the deterministic GroupId per ParsedTitle
        var matchingDecisions = allTvDecisions
            .Where(d => TvShowGroup.ComputeGroupId(request.ScanId, d.ParsedTitle!) == request.GroupId)
            .ToList();

        if (matchingDecisions.Count == 0)
            return Result.Fail<AssignTvGroupResult>(
                $"GROUP_NOT_FOUND: No TV show group with id '{request.GroupId}' found in scan '{request.ScanId}'.");

        var parsedShowName = matchingDecisions[0].ParsedTitle!;

        // Track old MediaIds (distinct) for orphan cleanup after the assignment
        var oldMediaIds = matchingDecisions
            .Where(d => d.MediaFile?.MediaId.HasValue == true)
            .Select(d => d.MediaFile!.MediaId!.Value)
            .Distinct()
            .ToList();

        // Verify the TMDB id exists as a TV show
        var lookup = await tmdbService.GetTvShowByIdAsync(request.TmdbId, cancellationToken: cancellationToken);

        if (lookup is null)
            return Result.Fail<AssignTvGroupResult>(
                $"TMDB_ID_NOT_FOUND: The TMDB id {request.TmdbId} does not correspond to a known TV show.");

        // Upsert the Media row once (shared by all decisions in the group)
        var media = await db.Medias
            .FirstOrDefaultAsync(
                m => m.TmdbId == lookup.TmdbId && m.Type == lookup.Kind,
                cancellationToken);

        if (media is null)
        {
            media = new Domain.Entities.Media
            {
                TmdbId = lookup.TmdbId,
                Title = lookup.Title,
                Type = lookup.Kind,
                Year = lookup.Year,
                PosterPath = lookup.PosterPath
            };
            db.Medias.Add(media);
            // Save now so Media.Id is available for MediaFile updates
            await db.SaveChangesAsync(cancellationToken);
        }

        var newMediaId = media.Id;

        // Bulk-update all member decisions
        foreach (var decision in matchingDecisions)
        {
            decision.AssignedTmdbId = lookup.TmdbId;
            decision.AssignedTmdbKind = lookup.Kind;

            if (decision.MediaFile is not null)
                decision.MediaFile.MediaId = newMediaId;
        }

        await db.SaveChangesAsync(cancellationToken);

        // Orphan cleanup: for each old Media row that is now no longer the new target,
        // check if any MediaFile still references it; if not, remove the orphaned row.
        var hadOrphans = false;
        foreach (var oldMediaId in oldMediaIds.Where(id => id != newMediaId))
        {
            var hasRemainingFiles = await db.MediaFiles
                .AnyAsync(f => f.MediaId == oldMediaId, cancellationToken);

            if (!hasRemainingFiles)
            {
                var oldMedia = await db.Medias.FindAsync([oldMediaId], cancellationToken);
                if (oldMedia is not null)
                {
                    db.Medias.Remove(oldMedia);
                    hadOrphans = true;
                }
            }
        }

        // Persist orphan deletions in a single final save (if any)
        if (hadOrphans)
            await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new AssignTvGroupResult(
            request.GroupId,
            parsedShowName,
            matchingDecisions.Count,
            lookup.TmdbId,
            lookup.Kind,
            lookup.Title,
            lookup.Year,
            lookup.PosterPath));
    }
}

