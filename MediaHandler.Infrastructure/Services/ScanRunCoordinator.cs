using System.Threading.Channels;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models.Scanner;
using Microsoft.Extensions.Logging;

namespace MediaHandler.Infrastructure.Services;

/// <summary>
/// Singleton coordinator that owns the lifecycle of background scan runs.
/// <para>
/// Enforces the single-active-scan invariant via an in-memory mutex backed
/// by the database filtered unique index on <c>ScanRun.Status = 'Running'</c>.
/// </para>
/// </summary>
/// <remarks>
/// Scan pipeline logic (<c>ScanPipeline</c>) is not yet implemented (US1 / T067).
/// Methods that require it throw <see cref="NotImplementedException"/> until that
/// phase is complete.
/// </remarks>
public sealed class ScanRunCoordinator : IScanRunCoordinator, IDisposable
{
    private readonly ILogger<ScanRunCoordinator> _logger;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    // keyed by ScanRunId
    private readonly Dictionary<Guid, (CancellationTokenSource Cts, Channel<ScanProgressDto> Channel)> _runs = new();

    public ScanRunCoordinator(ILogger<ScanRunCoordinator> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    /// <exception cref="NotImplementedException">Filled in during the US1 implementation phase.</exception>
    public Task<ScanRunHandle> StartAsync(ScanStartParameters parameters, CancellationToken ct = default)
    {
        throw new NotImplementedException(
            "ScanRunCoordinator.StartAsync will be implemented in the US1 phase (ScanPipeline). " +
            "This scaffold compiles but does not execute a real scan.");
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

