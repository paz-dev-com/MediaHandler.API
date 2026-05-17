namespace MediaHandler.Application.Common.DTOs;

/// <summary>
///     Result returned by the bulk review-item resolution endpoint.
/// </summary>
public record BulkResolveResult(
    /// <summary>Number of <c>ReviewItem</c> rows that were resolved in this operation.</summary>
    int ResolvedCount);

