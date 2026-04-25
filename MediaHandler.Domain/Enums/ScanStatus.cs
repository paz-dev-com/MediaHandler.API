namespace MediaHandler.Domain.Enums;

/// <summary>
/// Lifecycle state of a <c>ScanRun</c>.
/// Valid transitions: <c>Pending → Running → Completed | Failed | Cancelled</c>.
/// </summary>
public enum ScanStatus
{
    /// <summary>Queued but the background worker has not started processing yet.</summary>
    Pending,

    /// <summary>
    /// Currently executing. Enforced as a singleton by a filtered unique index
    /// (<c>WHERE Status = 'Running'</c>) on the <c>ScanRuns</c> table.
    /// </summary>
    Running,

    /// <summary>Finished without errors.</summary>
    Completed,

    /// <summary>Aborted due to an unrecoverable error; see <c>ScanRun.FailureReason</c>.</summary>
    Failed,

    /// <summary>Stopped by an explicit <c>CancelScan</c> request from an administrator.</summary>
    Cancelled
}

