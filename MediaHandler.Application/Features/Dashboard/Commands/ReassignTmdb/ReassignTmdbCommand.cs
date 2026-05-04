// ReassignTmdb — command, validator, and handler for correcting a TMDB match on a ScanItemDecision.
// Loads the decision, verifies the TMDB id via ITmdbService, updates AssignedTmdbId/Kind,
// updates the linked MediaFile.MediaId to point to the correct Media row, and saves.

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
///         <item>Upserts the linked <c>Media</c> row (TmdbId + Type) and points <c>MediaFile.MediaId</c> at it.</item>
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

        // Verify the TMDB id actually exists — try hinted kind first, then the other
        var lookup = request.MediaType == MediaType.Film
            ? await tmdbService.GetMovieByIdAsync(request.TmdbId, cancellationToken: cancellationToken)
            : await tmdbService.GetTvShowByIdAsync(request.TmdbId, cancellationToken: cancellationToken);

        if (lookup is null)
            lookup = request.MediaType == MediaType.Film
                ? await tmdbService.GetTvShowByIdAsync(request.TmdbId, cancellationToken: cancellationToken)
                : await tmdbService.GetMovieByIdAsync(request.TmdbId, cancellationToken: cancellationToken);

        if (lookup is null)
            return Result.Fail<ReassignTmdbResult>(
                $"TMDB_ID_NOT_FOUND: The TMDB id {request.TmdbId} does not correspond to a known movie or TV show.");

        // Update the decision
        decision.AssignedTmdbId = lookup.TmdbId;
        decision.AssignedTmdbKind = lookup.Kind;

        // Upsert the Media row and link MediaFile to it
        Guid? linkedMediaId = null;
        if (decision.MediaFile is not null)
        {
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
            }
            else
            {
                linkedMediaId = media.Id;
            }

            // For newly added Media, EF resolves the Id after SaveChanges
            decision.MediaFile.MediaId = media.Id == Guid.Empty ? null : media.Id;
        }

        await db.SaveChangesAsync(cancellationToken);

        // After save, the Media id is fully resolved (handles newly inserted rows)
        if (decision.MediaFile?.MediaId is not null)
            linkedMediaId = decision.MediaFile.MediaId;

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
