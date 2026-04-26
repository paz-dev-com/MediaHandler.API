using System.Threading.Channels;
using MediaHandler.Application.Common.Models.Scanner;

namespace MediaHandler.Application.Common.Interfaces;

/// <summary>
/// Singleton coordinator that owns the lifecycle of background scan runs.
/// Enforces the single-active-scan invariant and exposes progress streams
/// for polling / push endpoints.
/// </summary>
public interface IScanRunCoordinator
{
    /// <summary>
    /// Starts the scan described by <paramref name="parameters"/> in the background.
    /// Returns a <see cref="ScanRunHandle"/> immediately; the scan executes asynchronously.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown (or surfaced as <c>Result.Conflict</c>) when a scan is already running.
    /// </exception>
    Task<ScanRunHandle> StartAsync(ScanStartParameters parameters, CancellationToken ct = default);

    /// <summary>
    /// Signals the running scan identified by <paramref name="scanRunId"/> to stop.
    /// Idempotent — returns normally if the scan is already finished.
    /// </summary>
    Task RequestCancellationAsync(Guid scanRunId);

    /// <summary>
    /// Returns a <see cref="ChannelReader{T}"/> that delivers live progress updates
    /// for the scan identified by <paramref name="scanRunId"/>.
    /// Returns <c>null</c> when no such scan is known (already finished or never started).
    /// </summary>
    ChannelReader<ScanProgressDto>? Subscribe(Guid scanRunId);
}

