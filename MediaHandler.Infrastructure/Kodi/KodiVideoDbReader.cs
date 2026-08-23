// KodiVideoDbReader — Microsoft.Data.Sqlite-based reader for Kodi video database uploads.
// The uploaded file is untrusted input (FR-031): opened read-only, queried only with the
// hardcoded statements in KodiDbQueries, streamed via SqliteDataReader, never loaded fully
// into memory, never executed against the application's own database.

using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models.Kodi;
using MediaHandler.Infrastructure.Options;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MediaHandler.Infrastructure.Kodi;

public sealed class KodiVideoDbReader(
    IOptions<KodiImportOptions> options,
    ILogger<KodiVideoDbReader> logger) : IKodiVideoDbReader
{
    private const int DefensiveCommandTimeoutSeconds = 60;

    // SQLite result code for "file is not a database" (SQLITE_NOTADB).
    // SOURCE: SQLite documentation – Result Codes (https://sqlite.org/rescode.html#notadb)
    private const int SqliteNotADb = 26;

    /// <inheritdoc />
    public async Task<KodiDbValidationResult> ValidateAsync(
        string filePath, int schemaVersion, CancellationToken ct = default)
    {
        var supported = options.Value.SupportedSchemaVersions;
        if (!supported.Contains(schemaVersion))
        {
            return KodiDbValidationResult.Invalid(
                "UNSUPPORTED_VERSION",
                $"Unsupported Kodi database version {schemaVersion}. " +
                $"Supported versions: {string.Join(", ", supported)} (Kodi 19/20/21).");
        }

        var querySet = KodiDbQueries.ForVersion(schemaVersion);

        try
        {
            await using var connection = CreateReadOnlyConnection(filePath);
            await connection.OpenAsync(ct);

            // Required video-library tables must exist — this also rejects renamed music DBs.
            var tables = (await ReadStringListAsync(connection, KodiDbQueries.TableNamesQuery, ct: ct))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missingTables = querySet.RequiredTables.Where(t => !tables.Contains(t)).ToList();
            if (missingTables.Count > 0)
            {
                return KodiDbValidationResult.Invalid(
                    "INVALID_KODI_DB",
                    "The uploaded file is not a Kodi video database " +
                    $"(missing table(s): {string.Join(", ", missingTables)}). " +
                    "Upload the MyVideos<version>.db file from Kodi's userdata/Database folder.");
            }

            // Every column the queries reference must exist (a missing table yields zero rows
            // from pragma_table_info and therefore fails here with the table named).
            foreach (var (table, columns) in querySet.RequiredColumns)
            {
                var present = (await ReadStringListAsync(
                        connection, KodiDbQueries.ColumnNamesQuery, ct, ("@tableName", table)))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var missingColumns = columns.Where(c => !present.Contains(c)).ToList();
                if (missingColumns.Count > 0)
                {
                    return KodiDbValidationResult.Invalid(
                        "INVALID_KODI_DB",
                        $"The uploaded file does not match the Kodi {schemaVersion} video database schema " +
                        $"(table '{table}' is missing column(s): {string.Join(", ", missingColumns)}).");
                }
            }

            return KodiDbValidationResult.Valid();
        }
        catch (SqliteException ex)
        {
            logger.LogWarning(ex, "Kodi database validation failed for {FilePath}.", filePath);
            return KodiDbValidationResult.Invalid("INVALID_KODI_DB", SqliteErrorMessage(ex));
        }
    }

    /// <inheritdoc />
    public async Task<KodiLibrarySnapshot> ReadAsync(string filePath, int schemaVersion, CancellationToken ct = default)
    {
        var querySet = KodiDbQueries.ForVersion(schemaVersion);

        await using var connection = CreateReadOnlyConnection(filePath);
        await connection.OpenAsync(ct);

        var uniqueIds = await ReadUniqueIdsAsync(connection, querySet, ct);
        var movies = await ReadMoviesAsync(connection, querySet, uniqueIds, ct);
        var shows = await ReadShowsAsync(connection, querySet, uniqueIds, ct);
        var musicVideos = await ReadMusicVideosAsync(connection, querySet, ct);

        return new KodiLibrarySnapshot(movies, shows, musicVideos);
    }

    // =========================================================================
    // Query execution
    // =========================================================================

    private async Task<IReadOnlyList<KodiMovieItem>> ReadMoviesAsync(
        SqliteConnection connection,
        KodiQuerySet querySet,
        Dictionary<(string MediaType, int MediaId), List<KodiExternalId>> uniqueIds,
        CancellationToken ct)
    {
        var movies = new List<KodiMovieItem>();

        await using var command = CreateCommand(connection, querySet.MoviesQuery);
        await using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetInt32(0);
            var title = GetStringOrNull(reader, 1) ?? string.Empty;
            var originalTitle = GetStringOrNull(reader, 2);
            var yearText = GetStringOrNull(reader, 3);
            // v121+: year = leading component of the premiered date; v119: c07 integer text.
            // SOURCE: Kodi wiki – Databases/MyVideos, "movie" table (premiered / c07)
            var year = querySet.MovieYearFromPremieredColumn
                ? ParseYearPrefix(yearText)
                : ParseIntText(yearText);

            var fileName = GetStringOrNull(reader, 4) ?? string.Empty;
            var path = GetStringOrNull(reader, 5) ?? string.Empty;

            var externalIds = uniqueIds.TryGetValue(("movie", id), out var ids) ? ids : [];
            movies.Add(new KodiMovieItem(
                id, title, originalTitle, year, externalIds, ExpandFileRefs(path, fileName)));
        }

        return movies;
    }

    private async Task<IReadOnlyList<KodiShowItem>> ReadShowsAsync(
        SqliteConnection connection,
        KodiQuerySet querySet,
        Dictionary<(string MediaType, int MediaId), List<KodiExternalId>> uniqueIds,
        CancellationToken ct)
    {
        // Episodes first, grouped by owning show
        var episodesByShow = new Dictionary<int, List<KodiEpisodeItem>>();

        await using (var command = CreateCommand(connection, querySet.EpisodesQuery))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var idEpisode = reader.GetInt32(0);
                var idShow = reader.GetInt32(1);
                var title = GetStringOrNull(reader, 2);
                var season = ParseIntText(GetStringOrNull(reader, 3));
                var episode = ParseIntText(GetStringOrNull(reader, 4));

                if (season is null || episode is null)
                {
                    logger.LogWarning(
                        "Skipping Kodi episode {EpisodeId}: unparseable season/episode numbers.",
                        idEpisode);
                    continue;
                }

                var fileRef = JoinUri(GetStringOrNull(reader, 6) ?? string.Empty,
                    GetStringOrNull(reader, 5) ?? string.Empty);

                if (!episodesByShow.TryGetValue(idShow, out var list))
                    episodesByShow[idShow] = list = [];

                list.Add(new KodiEpisodeItem(idEpisode, season.Value, episode.Value, title, fileRef));
            }
        }

        var shows = new List<KodiShowItem>();

        await using (var command = CreateCommand(connection, querySet.TvShowsQuery))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var id = reader.GetInt32(0);
                var title = GetStringOrNull(reader, 1) ?? string.Empty;
                // SOURCE: Kodi wiki – Databases/MyVideos, "tvshow" table (c05 = first aired date text)
                var year = ParseYearPrefix(GetStringOrNull(reader, 2));

                var episodes = episodesByShow.TryGetValue(id, out var list)
                    ? list.OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).ToList()
                    : [];

                var externalIds = uniqueIds.TryGetValue(("tvshow", id), out var ids) ? ids : [];
                shows.Add(new KodiShowItem(id, title, year, externalIds, episodes));
            }
        }

        return shows;
    }

    private static async Task<IReadOnlyList<KodiMusicVideoItem>> ReadMusicVideosAsync(
        SqliteConnection connection, KodiQuerySet querySet, CancellationToken ct)
    {
        var musicVideos = new List<KodiMusicVideoItem>();

        await using var command = CreateCommand(connection, querySet.MusicVideosQuery);
        await using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
            musicVideos.Add(new KodiMusicVideoItem(reader.GetInt32(0), GetStringOrNull(reader, 1) ?? string.Empty));

        return musicVideos;
    }

    private static async Task<Dictionary<(string MediaType, int MediaId), List<KodiExternalId>>> ReadUniqueIdsAsync(
        SqliteConnection connection, KodiQuerySet querySet, CancellationToken ct)
    {
        var result = new Dictionary<(string, int), List<KodiExternalId>>();

        await using var command = CreateCommand(connection, querySet.UniqueIdsQuery);
        await using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            var mediaId = reader.GetInt32(0);
            var mediaType = (GetStringOrNull(reader, 1) ?? string.Empty).ToLowerInvariant();
            var provider = (GetStringOrNull(reader, 2) ?? string.Empty).ToLowerInvariant();
            var value = GetStringOrNull(reader, 3);

            if (value is null)
                continue;

            if (!result.TryGetValue((mediaType, mediaId), out var list))
                result[(mediaType, mediaId)] = list = [];

            list.Add(new KodiExternalId(provider, value));
        }

        return result;
    }

    private static async Task<List<string>> ReadStringListAsync(
        SqliteConnection connection, string sql, CancellationToken ct,
        params (string Name, object Value)[] parameters)
    {
        var values = new List<string>();

        await using var command = CreateCommand(connection, sql);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var value = GetStringOrNull(reader, 0);
            if (value is not null)
                values.Add(value);
        }

        return values;
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static SqliteConnection CreateReadOnlyConnection(string filePath)
    {
        // Read-only mode: Microsoft.Data.Sqlite never writes to the uploaded file.
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = filePath,
            Mode = SqliteOpenMode.ReadOnly
        };
        return new SqliteConnection(builder.ConnectionString);
    }

    private static SqliteCommand CreateCommand(SqliteConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = DefensiveCommandTimeoutSeconds;
        return command;
    }

    private static string? GetStringOrNull(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    /// <summary>
    ///     Expands a movie's file reference: a <c>stack://</c> filename lists every part URI
    ///     (each part is itself a complete URI); anything else yields a single
    ///     <c>strPath + strFilename</c> reference.
    /// </summary>
    // SOURCE: Kodi wiki – File stacking (Split Video Files); a stacked movie is stored as a single
    // files entry whose strFilename is a stack:// URI joining the part URIs with " , "
    // (observed files.strFilename format).
    private static IReadOnlyList<string> ExpandFileRefs(string strPath, string strFilename)
    {
        const string stackPrefix = "stack://";

        if (strFilename.StartsWith(stackPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return strFilename[stackPrefix.Length..]
                .Split(" , ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        return [JoinUri(strPath, strFilename)];
    }

    private static string JoinUri(string strPath, string strFilename)
    {
        if (strPath.Length == 0)
            return strFilename;

        var separator = strPath.EndsWith('/') || strPath.EndsWith('\\') ? "" : "/";
        return strPath + separator + strFilename;
    }

    private static int? ParseYearPrefix(string? dateText)
    {
        // Kodi date columns hold ISO-8601-ish text ("1999-03-31"); the year is the leading component.
        // SOURCE: Kodi wiki – Databases/MyVideos (premiered / first-aired columns are date text)
        if (string.IsNullOrWhiteSpace(dateText) || dateText.Length < 4)
            return null;

        return int.TryParse(dateText[..4], out var year) ? year : null;
    }

    private static int? ParseIntText(string? text)
    {
        return int.TryParse(text, out var value) ? value : null;
    }

    private static string SqliteErrorMessage(SqliteException ex)
    {
        if (ex.SqliteErrorCode == SqliteNotADb)
            return "The uploaded file is not a SQLite database.";

        return "The uploaded file could not be read as a Kodi database " +
               "(it may be corrupt, truncated, or was copied while Kodi was running — " +
               "close Kodi before copying the file).";
    }
}
