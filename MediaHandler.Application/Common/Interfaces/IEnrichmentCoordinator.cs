using MediaHandler.Application.Features.Dashboard.DTOs;

namespace MediaHandler.Application.Common.Interfaces;

/// <summary>
///     Singleton coordinator that owns the lifecycle of background batch TMDB enrichment runs.
///     Follows the same pattern as <see cref="IScanRunCoordinator" />.
///     <para>
///         At most one enrichment run may be active at a time; a filtered unique index on the
///         <c>EnrichmentRuns</c> table backs this invariant at the database level.
///     </para>
/// </summary>
public interface IEnrichmentCoordinator
{
    /// <summary>
    ///     Starts the enrichment run identified by <paramref name="enrichmentRunId" /> in the
    ///     background using a dedicated <c>Task.Run</c> via <c>IServiceScopeFactory</c>.
    ///     The method returns immediately; enrichment proceeds asynchronously.
    /// </summary>
    /// <param name="enrichmentRunId">
    ///     Primary key of the <c>EnrichmentRun</c> row that was pre-inserted by the
    ///     <c>StartEnrichmentCommand</c> handler before calling this method.
    /// </param>
    /// <param name="ct">Cancellation token (used for the fire-and-forget initiation only).</param>
    Task StartAsync(Guid enrichmentRunId, string? language = null, CancellationToken ct = default);

    /// <summary>
    ///     Returns the current status of the most recent enrichment run, or <c>null</c> if
    ///     no enrichment run has ever been recorded.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task<EnrichmentRunDto?> GetStatusAsync(CancellationToken ct = default);
}

