// KodiDbQueries — ALL Kodi video-database schema knowledge for the import feature, isolated in
// this single file per the repository no-GPL rule (R-001). Every constant cites the public Kodi
// wiki "Databases" documentation (https://kodi.wiki/view/Databases and
// https://kodi.wiki/view/Databases/MyVideos). No Kodi source code was consulted or copied.
//
// Column facts verified against the wiki (developer, 2026-06):
//   • movie:      idMovie PK, idFile FK→files, c00 = local title, c16 = original title,
//                 premiered = premiere date (v20/121+, year = leading date prefix);
//                 c07 = release year in v19/119, "Not Used" from v20/121 onwards.
//   • tvshow:     idShow PK, c00 = show title, c05 = first aired (premiered date text).
//   • episode:    idEpisode PK, idFile FK→files, idShow FK→tvshow, c00 = episode title,
//                 c12 = season number, c13 = episode number.
//   • files:      idFile PK, idPath FK→path, strFilename.
//   • path:       idPath PK, strPath (URI as seen by the Kodi box, e.g. smb://server/share/).
//   • uniqueid:   media_id, media_type ('movie'|'tvshow'|'episode'), type (provider), value.
//   • musicvideo: idMVideo PK, c00 = title.

namespace MediaHandler.Infrastructure.Kodi;

/// <summary>
///     The set of SQL statements and structural expectations for one Kodi schema version family.
///     Obtained via <see cref="KodiDbQueries.ForVersion" /> — the single extension point if a
///     per-version divergence is found.
/// </summary>
public sealed record KodiQuerySet(
    string MoviesQuery,
    bool MovieYearFromPremieredColumn,
    string TvShowsQuery,
    string EpisodesQuery,
    string UniqueIdsQuery,
    string MusicVideosQuery,
    IReadOnlyList<string> RequiredTables,
    IReadOnlyDictionary<string, IReadOnlyList<string>> RequiredColumns);

public static class KodiDbQueries
{
    // SOURCE: Kodi wiki – Databases/MyVideos (tables movie, tvshow, episode, files, path, uniqueid)
    private static readonly string[] RequiredTablesList =
        ["movie", "tvshow", "episode", "files", "path", "uniqueid"];

    // SOURCE: Kodi wiki – Databases/MyVideos, "movie" table
    // (idMovie PK; idFile FK→files; c00 local title; c16 original title; premiered = date premiered, v121+)
    private const string MoviesQueryV121 =
        """
        SELECT m.idMovie, m.c00, m.c16, m.premiered, f.strFilename, p.strPath
        FROM movie m
        JOIN files f ON f.idFile = m.idFile
        JOIN path p ON p.idPath = f.idPath
        """;

    // SOURCE: Kodi wiki – Databases/MyVideos, "movie" table; Kodi wiki – Databases (v19 and earlier:
    // "the column containing the movie's release year is labelled simply c07" — c07 is Not Used from v20/121 on)
    private const string MoviesQueryV119 =
        """
        SELECT m.idMovie, m.c00, m.c16, m.c07, f.strFilename, p.strPath
        FROM movie m
        JOIN files f ON f.idFile = m.idFile
        JOIN path p ON p.idPath = f.idPath
        """;

    // SOURCE: Kodi wiki – Databases/MyVideos, "tvshow" table (idShow PK; c00 show title; c05 first aired)
    private const string TvShowsQuery =
        """
        SELECT s.idShow, s.c00, s.c05
        FROM tvshow s
        """;

    // SOURCE: Kodi wiki – Databases/MyVideos, "episode" table
    // (idEpisode PK; idFile FK→files; idShow FK→tvshow; c00 episode title; c12 season number; c13 episode number)
    private const string EpisodesQuery =
        """
        SELECT e.idEpisode, e.idShow, e.c00, e.c12, e.c13, f.strFilename, p.strPath
        FROM episode e
        JOIN files f ON f.idFile = e.idFile
        JOIN path p ON p.idPath = f.idPath
        """;

    // SOURCE: Kodi wiki – Databases/MyVideos, "uniqueid" table
    // (media_id, media_type, type = provider, value = id at provider site;
    //  episode uniqueids are ignored — identity is resolved at show level only)
    private const string UniqueIdsQuery =
        """
        SELECT media_id, media_type, type, value
        FROM uniqueid
        WHERE media_type IN ('movie', 'tvshow')
        """;

    // SOURCE: Kodi wiki – Databases/MyVideos, "musicvideo" table (idMVideo PK; c00 title)
    // Rows are read for skip counting/reporting only — the app has no music-video media type.
    private const string MusicVideosQuery =
        """
        SELECT idMVideo, c00
        FROM musicvideo
        """;

    // Table inventory used by ValidateAsync to fingerprint a Kodi video database.
    // SOURCE: Kodi wiki – Databases/MyVideos (table list)
    internal const string TableNamesQuery = "SELECT name FROM sqlite_master WHERE type = 'table'";

    // Column inventory used by ValidateAsync; the table-valued PRAGMA accepts a parameter,
    // unlike the PRAGMA statement form.
    // SOURCE: SQLite documentation – PRAGMA table_info (https://sqlite.org/pragma.html#pragma_table_info)
    internal const string ColumnNamesQuery = "SELECT name FROM pragma_table_info(@tableName)";

    /// <summary>
    ///     Returns the query set for a schema version.
    ///     The used concepts are stable across Kodi 20/21 (121/131); Kodi 19 (119) predates the
    ///     <c>movie.premiered</c> column and reads the year from <c>movie.c07</c> instead.
    /// </summary>
    public static KodiQuerySet ForVersion(int version)
    {
        var movieYearFromPremiered = version >= 121;

        // SOURCE: Kodi wiki – Databases/MyVideos (per-table column lists)
        IReadOnlyDictionary<string, IReadOnlyList<string>> requiredColumns =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["movie"] = movieYearFromPremiered
                    ? ["idMovie", "idFile", "c00", "c16", "premiered"]
                    : ["idMovie", "idFile", "c00", "c16", "c07"],
                ["tvshow"] = ["idShow", "c00", "c05"],
                ["episode"] = ["idEpisode", "idFile", "idShow", "c00", "c12", "c13"],
                ["files"] = ["idFile", "idPath", "strFilename"],
                ["path"] = ["idPath", "strPath"],
                ["uniqueid"] = ["media_id", "media_type", "type", "value"],
                ["musicvideo"] = ["idMVideo", "c00"]
            };

        return new KodiQuerySet(
            movieYearFromPremiered ? MoviesQueryV121 : MoviesQueryV119,
            movieYearFromPremiered,
            TvShowsQuery,
            EpisodesQuery,
            UniqueIdsQuery,
            MusicVideosQuery,
            RequiredTablesList,
            requiredColumns);
    }
}
