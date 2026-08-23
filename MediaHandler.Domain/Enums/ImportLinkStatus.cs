namespace MediaHandler.Domain.Enums;

/// <summary>
///     File-link outcome of a Kodi import item.
///     <c>null</c> on the outcome row when no link was attempted or applicable.
/// </summary>
public enum ImportLinkStatus
{
    /// <summary>At least one new link was created and no part is missing.</summary>
    Linked,

    /// <summary>All required links were already present.</summary>
    AlreadyLinked,

    /// <summary>Stack: some parts linked, at least one part missing or unmatched (the reason names it).</summary>
    PartiallyLinked,

    /// <summary>No path mapping covers the Kodi prefix.</summary>
    UnmatchedPath,

    /// <summary>The path translated through a mapping but the scanner has no such file.</summary>
    NoScannedFile,

    /// <summary>Non-filesystem scheme (<c>pvr://</c>, <c>http://</c>, <c>upnp://</c>, …).</summary>
    UnsupportedLocation,

    /// <summary>The file is already linked to a different Media — preserved, never stolen.</summary>
    Conflict
}
