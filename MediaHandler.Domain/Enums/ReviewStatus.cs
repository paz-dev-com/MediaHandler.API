namespace MediaHandler.Domain.Enums;

/// <summary>
///     Resolution state of a <c>ReviewItem</c>.
/// </summary>
public enum ReviewStatus
{
    /// <summary>Awaiting administrator action.</summary>
    Open,

    /// <summary>Administrator assigned a TMDB id; the item is fully resolved.</summary>
    Resolved,

    /// <summary>Administrator acknowledged and dismissed the item without assigning TMDB metadata.</summary>
    Dismissed
}