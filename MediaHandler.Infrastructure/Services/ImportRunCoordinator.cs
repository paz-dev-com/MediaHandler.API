using System.Text.Json;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models.Kodi;
using MediaHandler.Application.Features.Dashboard.Commands.StartEnrichment;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Enums;
using MediaHandler.Infrastructure.Kodi;
using MediaHandler.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediaHandler.Infrastructure.Services;

/// <summary>
///     Singleton coordinator that owns the lifecycle of background Kodi import runs.
///     <para>
///         Enforces the single-active-import invariant via an in-memory mutex backed by the
///         database filtered unique index on <c>ImportRun.Status = 'Running'</c>. The run row is
///         inserted as Pending and transitioned to Running inside the mutex, closing the
///         Pending→Running race window; the filtered index remains the DB backstop.
///     </para>
///     <para>
///         Scoped services (<see cref="KodiImportPipeline" />, <see cref="MediaHandlerDbContext" />,
///         <see cref="IKodiImportFileStore" />) are resolved through <see cref="IServiceScopeFactory" />
///         to avoid captive-dependency issues.
///     </para>
/// </summary>
public sealed class ImportRunCoordinator(
    ILogger<ImportRunCoordinator> logger,
    IServiceScopeFactory scopeFactory)
    : IImportRunCoordinator
{
    private readonly SemaphoreSlim _mutex = new(1, 1);

    /// <inheritdoc />
    public async Task<KodiImportRunHandle> StartAsync(KodiImportStartParameters parameters, CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MediaHandlerDbContext>();

            // Single-active-import guard
            var activeRun = await db.ImportRuns
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Status == ImportRunStatus.Running, ct);

            if (activeRun is not null)
                throw new InvalidOperationException("IMPORT_IN_PROGRESS");

            var run = new ImportRun
            {
                Id = parameters.ImportRunId,
                Mode = parameters.Mode,
                Status = ImportRunStatus.Pending,
                SourceFileName = parameters.SourceFileName,
                SchemaVersion = parameters.SchemaVersion,
                UploadedFilePath = parameters.StoredFilePath,
                PathMappingsJson = JsonSerializer.Serialize(parameters.Mappings),
                StartedAt = DateTime.UtcNow
            };
            db.ImportRuns.Add(run);
            await db.SaveChangesAsync(ct);

            // Transition to Running inside the mutex (the filtered unique index is the backstop).
            try
            {
                run.Status = ImportRunStatus.Running;
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                // The Pending row lost the race to another Running import. Move it to a terminal
                // state so it does not stay Pending forever before we report the race to the caller.
                run.Status = ImportRunStatus.Failed;
                run.FinishedAt = DateTime.UtcNow;
                run.FailureReason = "IMPORT_IN_PROGRESS";
                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (Exception cleanupEx)
                {
                    logger.LogError(cleanupEx, "Failed to mark rejected pending import run {ImportRunId} as failed.",
                        parameters.ImportRunId);
                }

                throw new InvalidOperationException("IMPORT_IN_PROGRESS", ex);
            }

            // Fire and forget background import (owns its own DI scope).
            // No cancellation support in scope — the background task runs to completion.
            _ = ExecuteImportAsync(parameters);

            logger.LogInformation("ImportRunCoordinator: started import run {ImportRunId} (mode={Mode})",
                parameters.ImportRunId, parameters.Mode);

            return new KodiImportRunHandle(parameters.ImportRunId);
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task ExecuteImportAsync(KodiImportStartParameters parameters)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaHandlerDbContext>();
        var pipeline = scope.ServiceProvider.GetRequiredService<KodiImportPipeline>();
        var fileStore = scope.ServiceProvider.GetRequiredService<IKodiImportFileStore>();

        // Reload the run within this scope's DbContext so EF tracks it correctly.
        var run = await db.ImportRuns.FirstAsync(r => r.Id == parameters.ImportRunId);

        try
        {
            await pipeline.ExecuteAsync(run, parameters, CancellationToken.None);

            run.Status = ImportRunStatus.Completed;
            run.FinishedAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            run.Status = ImportRunStatus.Failed;
            run.FinishedAt = DateTime.UtcNow;
            run.FailureReason = ex.Message;
            logger.LogError(ex, "Import run {ImportRunId} failed.", parameters.ImportRunId);
        }
        finally
        {
            try
            {
                await db.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to persist final import run status for {ImportRunId}.",
                    parameters.ImportRunId);
            }

            // After a successful real import, trigger the existing TMDB enrichment pipeline so
            // imported entries (created with Overview = null) are automatically populated.
            // Preview runs and failed runs are skipped; enrichment failures are never allowed to
            // fail the import run itself.
            if (parameters.Mode == KodiImportMode.Import && run.Status == ImportRunStatus.Completed)
            {
                try
                {
                    var sender = scope.ServiceProvider.GetRequiredService<ISender>();
                    var enrichmentResult = await sender.Send(new StartEnrichmentCommand(), CancellationToken.None);

                    if (enrichmentResult.IsSuccess)
                    {
                        if (enrichmentResult.Value.WasStarted)
                        {
                            logger.LogInformation(
                                "Import run {ImportRunId} completed and triggered enrichment run {EnrichmentRunId} ({TotalItems} items).",
                                parameters.ImportRunId,
                                enrichmentResult.Value.EnrichmentRunId,
                                enrichmentResult.Value.TotalItems);
                        }
                        else
                        {
                            logger.LogInformation(
                                "Import run {ImportRunId} completed; no enrichment needed (no eligible items).",
                                parameters.ImportRunId);
                        }
                    }
                    else
                    {
                        var error = enrichmentResult.Errors.FirstOrDefault() ?? string.Empty;
                        if (error.StartsWith("ENRICHMENT_ALREADY_RUNNING", StringComparison.OrdinalIgnoreCase))
                        {
                            logger.LogInformation(
                                "Import run {ImportRunId} completed; enrichment is already running, " +
                                "imported entries will be collected by the next run.",
                                parameters.ImportRunId);
                        }
                        else
                        {
                            logger.LogError(
                                "Import run {ImportRunId} completed but enrichment could not be started: {Error}.",
                                parameters.ImportRunId,
                                error);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "Import run {ImportRunId} completed but the enrichment trigger failed unexpectedly.",
                        parameters.ImportRunId);
                }
            }

            // The uploaded file is discarded when the run reaches a terminal state;
            // a process crash between the two saves leaves an orphan cleaned by startup recovery.
            fileStore.Delete(run.UploadedFilePath);
            run.UploadedFilePath = null;

            try
            {
                await db.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to clear the uploaded file path for import run {ImportRunId}.",
                    parameters.ImportRunId);
            }
        }
    }
}
