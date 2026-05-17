// StartEnrichment — command and handler for initiating a background TMDB batch enrichment run.
// (1) Checks for an active Running row → conflict.
// (2) Counts eligible Media entries (Overview IS NULL OR UpdatedAt > last completed enrichment).
// (3) If count = 0 → returns 200 OK result (no row inserted, no 202).
// (4) Inserts an EnrichmentRun row with Status=Pending.
// (5) Triggers IEnrichmentCoordinator.StartAsync(runId) to begin background processing.

using FluentValidation;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Dashboard.Commands.StartEnrichment;

/// <summary>
///     Application-layer result returned by <see cref="StartEnrichmentCommandHandler" />.
///     <para>
///         <see cref="WasStarted" /> is <c>true</c> when a new run was created (→ HTTP 202 Accepted).
///         <see cref="WasStarted" /> is <c>false</c> when there is nothing to enrich (→ HTTP 200 OK).
///     </para>
/// </summary>
public record StartEnrichmentResult(
    bool WasStarted,
    Guid? EnrichmentRunId,
    EnrichmentStatus Status,
    int TotalItems);

/// <summary>Command to start a background TMDB batch enrichment run.</summary>
public record StartEnrichmentCommand(string? Language = null) : IRequest<Result<StartEnrichmentResult>>;

// =========================================================================
// Validator
// =========================================================================

public class StartEnrichmentCommandValidator : AbstractValidator<StartEnrichmentCommand>
{
    public StartEnrichmentCommandValidator()
    {
        // No fields to validate — the command carries no input parameters.
    }
}

// =========================================================================
// Handler
// =========================================================================

/// <summary>
///     Handles <see cref="StartEnrichmentCommand" />.
///     <list type="bullet">
///         <item>Returns <c>ENRICHMENT_ALREADY_RUNNING</c> failure when a <c>Running</c> row already exists.</item>
///         <item>Counts eligible <c>Media</c> entries; returns success with <c>WasStarted=false</c> when none found.</item>
///         <item>Inserts a <c>Pending</c> <c>EnrichmentRun</c> row and fires <see cref="IEnrichmentCoordinator.StartAsync" />.</item>
///     </list>
/// </summary>
public sealed class StartEnrichmentCommandHandler(
    IApplicationDbContext db,
    IEnrichmentCoordinator coordinator)
    : IRequestHandler<StartEnrichmentCommand, Result<StartEnrichmentResult>>
{
    public async Task<Result<StartEnrichmentResult>> Handle(
        StartEnrichmentCommand request,
        CancellationToken cancellationToken)
    {
        // (1) Check for active Running enrichment run
        var activeRun = await db.EnrichmentRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Status == EnrichmentStatus.Running, cancellationToken);

        if (activeRun is not null)
            return Result.Fail<StartEnrichmentResult>(
                "ENRICHMENT_ALREADY_RUNNING: An enrichment run is already in progress.");

        // Determine FinishedAt of the most recent Completed run (for incremental exclusion)
        var lastFinishedAt = await db.EnrichmentRuns
            .AsNoTracking()
            .Where(r => r.Status == EnrichmentStatus.Completed && r.FinishedAt.HasValue)
            .OrderByDescending(r => r.FinishedAt)
            .Select(r => r.FinishedAt)
            .FirstOrDefaultAsync(cancellationToken);

        // (2) Count eligible Media entries
        // Eligible = Overview IS NULL  OR  UpdatedAt > last completed enrichment
        var eligibleCount = await db.Medias
            .AsNoTracking()
            .Where(m => m.Overview == null
                        || (lastFinishedAt != null && m.UpdatedAt > lastFinishedAt))
            .CountAsync(cancellationToken);

        // (3) Nothing to do — return 200 OK without inserting a row
        if (eligibleCount == 0)
            return Result.Success(new StartEnrichmentResult(
                WasStarted: false,
                EnrichmentRunId: null,
                Status: EnrichmentStatus.Completed,
                TotalItems: 0));

        // (4) Insert EnrichmentRun with Status = Pending
        var run = new EnrichmentRun
        {
            Status = EnrichmentStatus.Pending,
            StartedAt = DateTime.UtcNow,
            TotalItems = eligibleCount
        };
        db.EnrichmentRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        // (5) Trigger background enrichment (fire-and-forget; coordinator owns the lifecycle)
        var language = string.IsNullOrWhiteSpace(request.Language) ? null : request.Language;
        await coordinator.StartAsync(run.Id, language, cancellationToken);

        return Result.Success(new StartEnrichmentResult(
            WasStarted: true,
            EnrichmentRunId: run.Id,
            Status: EnrichmentStatus.Pending,
            TotalItems: eligibleCount));
    }
}

