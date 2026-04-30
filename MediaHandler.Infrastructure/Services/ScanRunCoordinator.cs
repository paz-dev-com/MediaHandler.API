using System.Text.Json;
using System.Threading.Channels;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models.Scanner;
using MediaHandler.Domain.Enums;
using MediaHandler.Infrastructure.Nas.Scanner;
using MediaHandler.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaHandler.Infrastructure.Services;

/// <summary>
/// Singleton coordinator that owns the lifecycle of background scan runs.
/// <para>
/// Enforces the single-active-scan invariant via an in-memory mutex backed
/// by the database filtered unique index on <c>ScanRun.Status = 'Running'</c>.
/// </para>
/// </summary>
public sealed class ScanRunCoordinator : IScanRunCoordinator, IDisposable
{
    private readonly ILogger<ScanRunCoordinator> _logger;
    private readonly ScanPipeline _pipeline;
    private readonly MediaHandlerDbContext _db;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    // keyed by ScanRunId
    private readonly Dictionary<Guid, (CancellationTokenSource Cts, Channel<ScanProgressDto> Channel)> _runs = new();

    public ScanRunCoordinator(
        ILogger<ScanRunCoordinator> logger,
        ScanPipeline pipeline,
        MediaHandlerDbContext db)
    {
        _logger = logger;
        _pipeline = pipeline;
        _db = db;
    }

    /// <inheritdoc />
    public async Task<ScanRunHandle> StartAsync(ScanStartParameters parameters, CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct);
        try
        {
            // ── Single-active-scan guard ──────────────────────────────────────
            var activeRun = await _db.ScanRuns
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Status == ScanStatus.Running, ct);

            if (activeRun is not null)
                throw new InvalidOperationException("SCAN_IN_PROGRESS");

            // ── Create the ScanRun row ────────────────────────────────────────
            var rootIds = parameters.LibraryRootIds;
            var scanRun = new Domain.Entities.ScanRun
            {
                Id = parameters.ScanRunId,
                Mode = parameters.Mode,
                Status = ScanStatus.Pending,
                StartedAt = DateTime.UtcNow,
                LibraryRootIdsJson = JsonSerializer.Serialize(rootIds)
            };
            _db.ScanRuns.Add(scanRun);
            await _db.SaveChangesAsync(ct);

            // ── Create progress channel ───────────────────────────────────────
            var channel = Channel.CreateUnbounded<ScanProgressDto>(new UnboundedChannelOptions
            {
                SingleWriter = true,
                SingleReader = false,
                AllowSynchronousContinuations = false
            });
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _runs[parameters.ScanRunId] = (cts, channel);

            // ── Fire and forget background scan ──────────────────────────────
            _ = ExecuteScanAsync(scanRun, parameters, cts, channel);

            _logger.LogInformation("ScanRunCoordinator: started scan run {ScanRunId} (mode={Mode})",
                parameters.ScanRunId, parameters.Mode);

            return new ScanRunHandle(parameters.ScanRunId);
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task ExecuteScanAsync(
        Domain.Entities.ScanRun scanRun,
        ScanStartParameters parameters,
        CancellationTokenSource cts,
        Channel<ScanProgressDto> channel)
    {
        try
        {
            // Mark as Running
            scanRun.Status = ScanStatus.Running;
            await _db.SaveChangesAsync(cts.Token);

            // Load roots
            var rootIds = parameters.LibraryRootIds;
            var roots = rootIds.Length == 0
                ? await _db.LibraryRoots.Where(r => r.IsEnabled).ToListAsync(cts.Token)
                : await _db.LibraryRoots.Where(r => rootIds.Contains(r.Id) && r.IsEnabled).ToListAsync(cts.Token);

            await _pipeline.ExecuteAsync(scanRun, roots, channel.Writer, cts.Token);

            scanRun.Status = ScanStatus.Completed;
            scanRun.FinishedAt = DateTime.UtcNow;
        }
        catch (OperationCanceledException)
        {
            scanRun.Status = ScanStatus.Cancelled;
            scanRun.FinishedAt = DateTime.UtcNow;
            _logger.LogInformation("Scan run {ScanRunId} cancelled.", scanRun.Id);
        }
        catch (Exception ex)
        {
            scanRun.Status = ScanStatus.Failed;
            scanRun.FinishedAt = DateTime.UtcNow;
            scanRun.FailureReason = ex.Message;
            _logger.LogError(ex, "Scan run {ScanRunId} failed.", scanRun.Id);
        }
        finally
        {
            try { await _db.SaveChangesAsync(CancellationToken.None); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist final scan run status for {ScanRunId}.", scanRun.Id);
            }

            channel.Writer.TryComplete();
            _runs.Remove(scanRun.Id);
            cts.Dispose();
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
            _logger.LogDebug("RequestCancellationAsync: scan run {ScanRunId} not found (already finished or never started).", scanRunId);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ChannelReader<ScanProgressDto>? Subscribe(Guid scanRunId) =>
        _runs.TryGetValue(scanRunId, out var entry) ? entry.Channel.Reader : null;

    public void Dispose()
    {
        foreach (var (cts, _) in _runs.Values)
            cts.Dispose();

        _mutex.Dispose();
    }
}

