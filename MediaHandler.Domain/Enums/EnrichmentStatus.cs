namespace MediaHandler.Domain.Enums;

/// <summary>
///     Lifecycle state of a <see cref="MediaHandler.Domain.Entities.EnrichmentRun" />.
/// </summary>
public enum EnrichmentStatus
{
    /// <summary>Run has been created but processing has not yet started.</summary>
    Pending,

    /// <summary>Run is actively processing media entries.</summary>
    Running,

    /// <summary>Run finished successfully (all items processed).</summary>
    Completed,

    /// <summary>Run terminated due to an unrecoverable error or crash recovery.</summary>
    Failed
}

