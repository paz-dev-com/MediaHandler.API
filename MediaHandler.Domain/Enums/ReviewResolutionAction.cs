namespace MediaHandler.Domain.Enums;

/// <summary>
/// The action an administrator takes when resolving an open <c>ReviewItem</c>.
/// Referenced by the review-items API contract.
/// </summary>
public enum ReviewResolutionAction
{
    /// <summary>
    /// Assign a specific TMDB id to the file, persisting the resolution so subsequent
    /// scans re-use it without re-querying TMDB title search.
    /// </summary>
    Assign,

    /// <summary>Acknowledge the item without assigning metadata; marks it <c>Dismissed</c>.</summary>
    Dismiss,

    /// <summary>
    /// Remove the underlying <c>MediaFile</c> row (and its orphaned parents if applicable)
    /// from the database; sets the review item to <c>Dismissed</c>.
    /// </summary>
    Delete
}

