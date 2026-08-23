namespace MediaHandler.Domain.Enums;

/// <summary>
///     The kind of a Kodi video-database library row as surfaced by the import.
/// </summary>
public enum KodiItemKind
{
    /// <summary>A row of the Kodi <c>movie</c> table.</summary>
    Movie,

    /// <summary>A row of the Kodi <c>tvshow</c> table.</summary>
    TvShow,

    /// <summary>A row of the Kodi <c>episode</c> table.</summary>
    Episode,

    /// <summary>A row of the Kodi <c>musicvideo</c> table (never imported; counted as skipped).</summary>
    MusicVideo
}
