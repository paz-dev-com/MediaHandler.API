namespace MediaHandler.Domain.Enums;

/// <summary>
///     Describes the functional role a physical file plays within a logical media item.
/// </summary>
public enum MediaFileRole
{
    /// <summary>The sole file for a single-file movie or the primary file for a TV episode.</summary>
    Main,

    /// <summary>One part of a multi-part (stacked) movie (cd1, cd2, disc1, disc2, …).</summary>
    StackedPart,

    /// <summary>A TV-episode file linked via <c>EpisodeFileLink</c>.</summary>
    Episode
}