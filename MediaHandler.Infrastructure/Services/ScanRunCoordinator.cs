using System.Text.Json;
using System.Threading.Channels;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models.Scanner;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Enums;
using MediaHandler.Infrastructure.Nas.Scanner;
using MediaHandler.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediaHandler.Infrastructure.Services;

/// <summary>
///     Singleton coordinator that owns the lifecycle of background scan runs.
///     <para>
///         Enforces the single-active-scan invariant via an in-memory mutex backed
///         by the database filtered unique index on <c>ScanRun.Status = 'Running'</c>.
///     </para>
///     <para>
///         Scoped services (<see cref="ScanPipeline" />, <see cref="MediaHandlerDbContext" />) are
///         resolved through <see cref="IServiceScopeFactory" /> to avoid captive-dependency issues.
///         A new DI scope is created for each operation so that every scan run gets a fresh
///         <c>DbContext</c> and pipeline instance.
///     </para>
/// </summary>
public sealed class ScanRunCoordinator : IScanRunCoordinator, IDisposable
{
    private readonly ILogger<ScanRunCoordinator> _logger;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    // keyed by ScanRunId
    private readonly Dictionary<Guid, (CancellationTokenSource Cts, Channel<ScanProgressDto> Channel)> _runs = new();
    private readonly IServiceScopeFactory _scopeFactory;

    public ScanRunCoordinator(
        ILogger<ScanRunCoordinator> logger,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public void Dispose()
    {
        foreach (var (cts, _) in _runs.Values)
            cts.Dispose();

        _mutex.Dispose();
    }

    /// <inheritdoc />
    public async Task<ScanRunHandle> StartAsync(ScanStartParameters parameters, CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MediaHandlerDbContext>();

            // ── Single-active-scan guard ──────────────────────────────────────
            var activeRun = await db.ScanRuns
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Status == ScanStatus.Running, ct);

            if (activeRun is not null)
                throw new InvalidOperationException("SCAN_IN_PROGRESS");

            // ── Create the ScanRun row ────────────────────────────────────────
            var rootIds = parameters.LibraryRootIds;
            var scanRun = new ScanRun
            {
                Id = parameters.ScanRunId,
                Mode = parameters.Mode,
                Status = ScanStatus.Pending,
                StartedAt = DateTime.UtcNow,
                LibraryRootIdsJson = JsonSerializer.Serialize(rootIds)
            };
            db.ScanRuns.Add(scanRun);
            await db.SaveChangesAsync(ct);

            // ── Create progress channel ───────────────────────────────────────
            var channel = Channel.CreateUnbounded<ScanProgressDto>(new UnboundedChannelOptions
            {
                SingleWriter = true,
                SingleReader = false,
                AllowSynchronousContinuations = false
            });
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _runs[parameters.ScanRunId] = (cts, channel);

            // ── Fire and forget background scan (owns its own DI scope) ───────
            _ = ExecuteScanAsync(parameters.ScanRunId, parameters, cts, channel);

            _logger.LogInformation("ScanRunCoordinator: started scan run {ScanRunId} (mode={Mode})",
                parameters.ScanRunId, parameters.Mode);

            return new ScanRunHandle(parameters.ScanRunId);
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <inheritdoc />
    public Task RequestCancellationAsync(Guid scanRunId)
    {
        if (_runs.TryGetValue(scanRunId, out var entry))
        {
            _logger.LogInformation("Cancellation requested for scan run {ScanRunId}.", scanRunId);
            entry.Cts.Cancel();
        }
        else
        {
            _logger.LogDebug(
                "RequestCancellationAsync: scan run {ScanRunId} not found (already finished or never started).",
                scanRunId);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ChannelReader<ScanProgressDto>? Subscribe(Guid scanRunId)
    {
        return _runs.TryGetValue(scanRunId, out var entry) ? entry.Channel.Reader : null;
    }

    private async Task ExecuteScanAsync(
        Guid scanRunId,
        ScanStartParameters parameters,
        CancellationTokenSource cts,
        Channel<ScanProgressDto> channel)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaHandlerDbContext>();
        var pipeline = scope.ServiceProvider.GetRequiredService<ScanPipeline>();

        // Reload the ScanRun within this scope's DbContext so EF tracks it correctly.
        var scanRun = await db.ScanRuns.FirstAsync(r => r.Id == scanRunId, cts.Token);

        try
        {
            // Mark as Running
            scanRun.Status = ScanStatus.Running;
            await db.SaveChangesAsync(cts.Token);

            // Load roots
            var rootIds = parameters.LibraryRootIds;
            var roots = rootIds.Length == 0
                ? await db.LibraryRoots.Where(r => r.IsEnabled).ToListAsync(cts.Token)
                : await db.LibraryRoots.Where(r => rootIds.Contains(r.Id) && r.IsEnabled).ToListAsync(cts.Token);

            await pipeline.ExecuteAsync(scanRun, roots, channel.Writer, cts.Token);

            scanRun.Status = ScanStatus.Completed;
            scanRun.FinishedAt = DateTime.UtcNow;
        }
        catch (OperationCanceledException)
        {
            scanRun.Status = ScanStatus.Cancelled;
            scanRun.FinishedAt = DateTime.UtcNow;
            _logger.LogInformation("Scan run {ScanRunId} cancelled.", scanRunId);
        }
        catch (Exception ex)
        {
            scanRun.Status = ScanStatus.Failed;
            scanRun.FinishedAt = DateTime.UtcNow;
            scanRun.FailureReason = ex.Message;
            _logger.LogError(ex, "Scan run {ScanRunId} failed.", scanRunId);
        }
        finally
        {
            try
            {
                await db.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist final scan run status for {ScanRunId}.", scanRunId);
            }

            channel.Writer.TryComplete();
            _runs.Remove(scanRunId);
            cts.Dispose();
        }
    }
}