// ReassignTmdb — command, validator, and handler for correcting a TMDB match on a ScanItemDecision.
// Loads the decision, verifies the TMDB id via ITmdbService, updates AssignedTmdbId/Kind,
// updates the linked MediaFile.MediaId to point to the correct Media row, and saves.
// T057 fix: tracks old MediaId and removes orphaned Media rows when no files remain.

using FluentValidation;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Dashboard.Commands.ReassignTmdb;

/// <summary>Application-layer result returned by <see cref="ReassignTmdbCommandHandler" />.</summary>
public record ReassignTmdbResult(
    Guid Id,
    int? AssignedTmdbId,
    MediaType? AssignedTmdbKind,
    string? AssignedTitle,
    int? AssignedYear,
    Guid? MediaFileId,
    Guid? MediaId);

/// <summary>Command to reassign the TMDB entry for a <c>ScanItemDecision</c>.</summary>
public record ReassignTmdbCommand(
    Guid DecisionId,
    int TmdbId,
    MediaType MediaType) : IRequest<Result<ReassignTmdbResult>>;

// =========================================================================
// Validator
// =========================================================================

public class ReassignTmdbCommandValidator : AbstractValidator<ReassignTmdbCommand>
{
    public ReassignTmdbCommandValidator()
    {
        RuleFor(x => x.DecisionId)
            .NotEmpty().WithMessage("DecisionId is required.");

        RuleFor(x => x.TmdbId)
            .GreaterThan(0).WithMessage("TmdbId must be a positive integer.");

        RuleFor(x => x.MediaType)
            .IsInEnum().WithMessage("MediaType must be Film or TvShow.");
    }
}

// =========================================================================
// Handler
// =========================================================================

/// <summary>
///     Handles <see cref="ReassignTmdbCommand" />.
///     <list type="bullet">
///         <item>Loads the <c>ScanItemDecision</c>; returns failure if not found.</item>
///         <item>Verifies the TMDB id exists via <see cref="ITmdbService" />.</item>
///         <item>Updates <c>AssignedTmdbId</c> and <c>AssignedTmdbKind</c> on the decision.</item>
///         <item>Looks up an existing <c>Media</c> row for the new TMDB ID, or creates one.</item>
///         <item>Updates <c>MediaFile.MediaId</c> to point at the new/existing <c>Media</c> row.</item>
///         <item>Removes the old <c>Media</c> row if it becomes orphaned (no remaining <c>MediaFile</c> references).</item>
///         <item>Saves all changes atomically.</item>
///     </list>
/// </summary>
public sealed class ReassignTmdbCommandHandler(
    IApplicationDbContext db,
    ITmdbService tmdbService)
    : IRequestHandler<ReassignTmdbCommand, Result<ReassignTmdbResult>>
{
    public async Task<Result<ReassignTmdbResult>> Handle(
        ReassignTmdbCommand request,
        CancellationToken cancellationToken)
    {
        // Load the decision (include MediaFile for updating MediaId)
        var decision = await db.ScanItemDecisions
            .Include(d => d.MediaFile)
            .FirstOrDefaultAsync(d => d.Id == request.DecisionId, cancellationToken);

        if (decision is null)
            return Result.Fail<ReassignTmdbResult>(
                $"DECISION_NOT_FOUND: ScanItemDecision '{request.DecisionId}' was not found.");

        // Track the old MediaId before making any changes, for orphan cleanup later
        var oldMediaId = decision.MediaFile?.MediaId;

        // Verify the TMDB id exists for the EXACT requested media type.
        // IMPORTANT: TMDB movie IDs and TV show IDs are independent namespaces — the same numeric
        // ID can point to completely different entries in each namespace. Never fall back to the
        // other type: doing so would silently assign a wrong film when the TV endpoint returns null.
        var lookup = request.MediaType == MediaType.Film
            ? await tmdbService.GetMovieByIdAsync(request.TmdbId, cancellationToken: cancellationToken)
            : await tmdbService.GetTvShowByIdAsync(request.TmdbId, cancellationToken: cancellationToken);

        if (lookup is null)
            return Result.Fail<ReassignTmdbResult>(
                $"TMDB_ID_NOT_FOUND: The TMDB id {request.TmdbId} does not correspond to a known {request.MediaType}.");

        // Update the decision assignment fields
        decision.AssignedTmdbId = lookup.TmdbId;
        decision.AssignedTmdbKind = lookup.Kind;

        // Upsert the Media row and link MediaFile to it
        Guid? linkedMediaId = null;
        if (decision.MediaFile is not null)
        {
            // Look up existing Media row for the new TMDB ID — reuse if found, create new if not
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
                // Save immediately so the Media.Id is persisted before linking MediaFile.
                // This also reduces the race-condition window when multiple reassign calls
                // arrive concurrently for the same TmdbId (though TV groups should use
                // the AssignTvGroup batch endpoint instead to avoid N parallel inserts).
                await db.SaveChangesAsync(cancellationToken);
            }

            linkedMediaId = media.Id;
            decision.MediaFile.MediaId = media.Id;
        }

        await db.SaveChangesAsync(cancellationToken);

        // Orphan cleanup: if the old Media row has no remaining MediaFile references, remove it.
        // This prevents stale enrichment data from accumulating when files are reassigned.
        if (oldMediaId.HasValue && oldMediaId != linkedMediaId)
        {
            var hasRemainingFiles = await db.MediaFiles
                .AnyAsync(f => f.MediaId == oldMediaId.Value, cancellationToken);

            if (!hasRemainingFiles)
            {
                var oldMedia = await db.Medias.FindAsync([oldMediaId.Value], cancellationToken);
                if (oldMedia is not null)
                    db.Medias.Remove(oldMedia);

                await db.SaveChangesAsync(cancellationToken);
            }
        }

        return Result.Success(new ReassignTmdbResult(
            decision.Id,
            decision.AssignedTmdbId,
            decision.AssignedTmdbKind,
            lookup.Title,
            lookup.Year,
            decision.MediaFileId,
            linkedMediaId));
    }
}
