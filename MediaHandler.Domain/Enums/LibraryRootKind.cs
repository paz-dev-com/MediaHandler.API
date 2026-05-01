namespace MediaHandler.Domain.Enums;

/// <summary>
///     Classifies the type of media content hosted under a <c>LibraryRoot</c>.
/// </summary>
public enum LibraryRootKind
{
    /// <summary>Root contains movie files only.</summary>
    Movies,

    /// <summary>Root contains TV-show files only.</summary>
    TvShows,

    /// <summary>Root contains a mixture of movies and TV shows.</summary>
    Mixed
}