using MediaHandler.Application.Common.Models.Kodi;

namespace MediaHandler.Application.Common.Interfaces;

/// <summary>
///     Singleton coordinator that owns the lifecycle of background Kodi import runs.
///     Enforces the single-active-import invariant (an import and a preview cannot run
///     concurrently). Progress is polled from the run row, whose counters are persisted
///     in batches — there is deliberately no progress channel and no cancellation.
/// </summary>
public interface IImportRunCoordinator
{
    /// <summary>
    ///     Starts the import described by <paramref name="parameters" /> in the background.
    ///     Returns a <see cref="KodiImportRunHandle" /> immediately; the import executes asynchronously.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     Thrown with message <c>IMPORT_IN_PROGRESS</c> when a run is already active.
    /// </exception>
    Task<KodiImportRunHandle> StartAsync(KodiImportStartParameters parameters, CancellationToken ct = default);
}
