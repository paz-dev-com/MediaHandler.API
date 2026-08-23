namespace MediaHandler.Domain.Enums;

/// <summary>
///     Determines whether a <c>ImportRun</c> persists domain changes or only projects them.
/// </summary>
public enum KodiImportMode
{
    /// <summary>Full import: creates Media/season/episode rows, links files, writes review items.</summary>
    Import,

    /// <summary>
    ///     Dry run: projects outcomes without touching domain data and without provider traffic.
    ///     Only the run and its per-item outcome rows are persisted.
    /// </summary>
    Preview
}
