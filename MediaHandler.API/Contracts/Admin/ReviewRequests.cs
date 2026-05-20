using MediaHandler.Domain.Enums;

namespace MediaHandler.API.Contracts.Admin;

/// <summary>
///     Request body for <c>POST /api/v1/admin/review-items/{id}/resolve</c>.
/// </summary>
public record ResolveReviewRequest(
    /// <summary>
    /// The resolution action to perform.
    /// <list type="bullet">
    ///   <item><see cref="ReviewResolutionAction.Assign"/>: bind the item to a specific TMDB id.</item>
    ///   <item><see cref="ReviewResolutionAction.Dismiss"/>: acknowledge without mapping.</item>
    ///   <item><see cref="ReviewResolutionAction.Delete"/>: remove the underlying file record.</item>
    /// </list>
    /// </summary>
    ReviewResolutionAction Action,
    /// <summary>Required when <see cref="Action"/> is <see cref="ReviewResolutionAction.Assign"/>.</summary>
    int? TmdbId,
    /// <summary>Required when <see cref="Action"/> is <see cref="ReviewResolutionAction.Assign"/>.</summary>
    MediaType? Kind);

/// <summary>
///     Request body for <c>POST /api/v1/admin/review-items/bulk-resolve</c>.
/// </summary>
public record BulkResolveReviewRequest(
    /// <summary>Absolute parent folder path — all Open review items under this path will be resolved.</summary>
    string ParentFolderPath,
    /// <summary>Resolution action to apply to every matched item.</summary>
    ReviewResolutionAction Action,
    /// <summary>Required when <see cref="Action"/> is <see cref="ReviewResolutionAction.Assign"/>.</summary>
    int? TmdbId,
    /// <summary>Required when <see cref="Action"/> is <see cref="ReviewResolutionAction.Assign"/>.</summary>
    MediaType? Kind);

/// <summary>
///     Request body for <c>POST /api/v1/admin/review-items/batch-assign</c>.
/// </summary>
public record BatchAssignReviewItemsRequest(
    /// <summary>IDs of the review items to assign.</summary>
    Guid[] ReviewItemIds,
    /// <summary>Internal Media.Id of the target media record.</summary>
    Guid TargetMediaId);
