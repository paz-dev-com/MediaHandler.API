namespace MediaHandler.Domain.Enums;

/// <summary>
///     Lifecycle state of an <c>ImportRun</c>.
///     Valid transitions: <c>Pending → Running → Completed | Failed</c>.
///     (No <c>Cancelled</c> — there is no cancel endpoint in scope.)
/// </summary>
public enum ImportRunStatus
{
    /// <summary>Queued but the background worker has not started processing yet.</summary>
    Pending,

    /// <summary>
    ///     Currently executing. Enforced as a singleton by a filtered unique index
    ///     (<c>WHERE Status = 'Running'</c>) on the <c>ImportRuns</c> table.
    /// </summary>
    Running,

    /// <summary>Finished without errors (partial item failures are reported per item).</summary>
    Completed,

    /// <summary>Aborted due to an unrecoverable error; see <c>ImportRun.FailureReason</c>.</summary>
    Failed
}
