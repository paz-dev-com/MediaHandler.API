using Microsoft.Data.Sqlite;

namespace MediaHandler.IntegrationTests.KodiImport;

public sealed record TestKodiMovie(
    int Id,
    string Title,
    string? OriginalTitle = null,
    int? Year = null,
    string Directory = "smb://server/share/Movies/",
    string FileName = "movie.mkv");

public sealed record TestKodiEpisode(
    int Id,
    int Season,
    int Episode,
    string? Title,
    string Directory,
    string FileName);

public sealed record TestKodiShow(
    int Id,
    string Title,
    string? FirstAired = null,
    IReadOnlyList<TestKodiEpisode>? Episodes = null);

public sealed record TestKodiMusicVideo(int Id, string Title);

public sealed record TestKodiUniqueId(int MediaId, string MediaType, string Type, string Value);

public static class KodiTestDbBuilder
{
    public static string CreateVideoDb(
        int schemaVersion = 121,
        IEnumerable<TestKodiMovie>? movies = null,
        IEnumerable<TestKodiShow>? shows = null,
        IEnumerable<TestKodiMusicVideo>? musicVideos = null,
        IEnumerable<TestKodiUniqueId>? uniqueIds = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"kodi-fixture-{Guid.NewGuid()}.db");
        CreateVideoDb(path, schemaVersion, movies, shows, musicVideos, uniqueIds);
        return path;
    }

    public static void CreateVideoDb(
        string path,
        int schemaVersion = 121,
        IEnumerable<TestKodiMovie>? movies = null,
        IEnumerable<TestKodiShow>? shows = null,
        IEnumerable<TestKodiMusicVideo>? musicVideos = null,
        IEnumerable<TestKodiUniqueId>? uniqueIds = null)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();

        CreateTables(connection, schemaVersion);

        var pathIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var fileIds = new Dictionary<(string Directory, string FileName), int>();

        int FileIdFor(string directory, string fileName)
        {
            if (fileIds.TryGetValue((directory, fileName), out var existing))
                return existing;

            if (!pathIds.TryGetValue(directory, out var pathId))
            {
                pathId = InsertAndGetId(connection,
                    "INSERT INTO path (strPath) VALUES ($strPath)",
                    ("$strPath", directory));
                pathIds[directory] = pathId;
            }

            var fileId = InsertAndGetId(connection,
                "INSERT INTO files (idPath, strFilename) VALUES ($idPath, $strFilename)",
                ("$idPath", pathId), ("$strFilename", fileName));
            fileIds[(directory, fileName)] = fileId;
            return fileId;
        }

        foreach (var movie in movies ?? [])
        {
            var fileId = FileIdFor(movie.Directory, movie.FileName);
            if (schemaVersion >= 121)
            {
                Execute(connection,
                    "INSERT INTO movie (idMovie, idFile, c00, c16, premiered) " +
                    "VALUES ($id, $idFile, $c00, $c16, $premiered)",
                    ("$id", movie.Id), ("$idFile", fileId), ("$c00", movie.Title),
                    ("$c16", movie.OriginalTitle),
                    ("$premiered", movie.Year.HasValue ? $"{movie.Year.Value}-01-01" : null));
            }
            else
            {
                Execute(connection,
                    "INSERT INTO movie (idMovie, idFile, c00, c16, c07) " +
                    "VALUES ($id, $idFile, $c00, $c16, $c07)",
                    ("$id", movie.Id), ("$idFile", fileId), ("$c00", movie.Title),
                    ("$c16", movie.OriginalTitle),
                    ("$c07", movie.Year?.ToString()));
            }
        }

        foreach (var show in shows ?? [])
        {
            Execute(connection,
                "INSERT INTO tvshow (idShow, c00, c05) VALUES ($id, $c00, $c05)",
                ("$id", show.Id), ("$c00", show.Title), ("$c05", show.FirstAired));

            foreach (var episode in show.Episodes ?? [])
            {
                var fileId = FileIdFor(episode.Directory, episode.FileName);
                Execute(connection,
                    "INSERT INTO episode (idEpisode, idFile, idShow, c00, c12, c13) " +
                    "VALUES ($id, $idFile, $idShow, $c00, $c12, $c13)",
                    ("$id", episode.Id), ("$idFile", fileId), ("$idShow", show.Id),
                    ("$c00", episode.Title),
                    ("$c12", episode.Season.ToString()), ("$c13", episode.Episode.ToString()));
            }
        }

        foreach (var musicVideo in musicVideos ?? [])
        {
            Execute(connection,
                "INSERT INTO musicvideo (idMVideo, c00) VALUES ($id, $c00)",
                ("$id", musicVideo.Id), ("$c00", musicVideo.Title));
        }

        foreach (var uniqueId in uniqueIds ?? [])
        {
            Execute(connection,
                "INSERT INTO uniqueid (media_id, media_type, type, value) " +
                "VALUES ($mediaId, $mediaType, $type, $value)",
                ("$mediaId", uniqueId.MediaId), ("$mediaType", uniqueId.MediaType),
                ("$type", uniqueId.Type), ("$value", uniqueId.Value));
        }
    }

    public static string CreateGarbageFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kodi-fixture-{Guid.NewGuid()}.db");
        File.WriteAllText(path, "This is definitely not a SQLite database file. " + new string('x', 2048));
        return path;
    }

    public static string CreateSqliteWithoutVideoTables()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kodi-fixture-{Guid.NewGuid()}.db");
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        Execute(connection, "CREATE TABLE artist (idArtist INTEGER PRIMARY KEY, strArtist TEXT)");
        Execute(connection, "INSERT INTO artist (strArtist) VALUES ('Someone')");
        return path;
    }

    public static void Delete(string? path)
    {
        if (path is not null && File.Exists(path))
            File.Delete(path);
    }

    private static void CreateTables(SqliteConnection connection, int schemaVersion)
    {
        Execute(connection, "CREATE TABLE path (idPath INTEGER PRIMARY KEY, strPath TEXT)");
        Execute(connection, "CREATE TABLE files (idFile INTEGER PRIMARY KEY, idPath INTEGER, strFilename TEXT)");

        if (schemaVersion >= 121)
        {
            Execute(connection,
                "CREATE TABLE movie (idMovie INTEGER PRIMARY KEY, idFile INTEGER, " +
                "c00 TEXT, c16 TEXT, c07 TEXT, premiered TEXT)");
        }
        else
        {
            Execute(connection,
                "CREATE TABLE movie (idMovie INTEGER PRIMARY KEY, idFile INTEGER, " +
                "c00 TEXT, c16 TEXT, c07 TEXT)");
        }

        Execute(connection, "CREATE TABLE tvshow (idShow INTEGER PRIMARY KEY, c00 TEXT, c05 TEXT)");
        Execute(connection,
            "CREATE TABLE episode (idEpisode INTEGER PRIMARY KEY, idFile INTEGER, idShow INTEGER, " +
            "c00 TEXT, c12 TEXT, c13 TEXT)");
        Execute(connection,
            "CREATE TABLE uniqueid (uniqueid INTEGER PRIMARY KEY, media_id INTEGER, " +
            "media_type TEXT, type TEXT, value TEXT)");
        Execute(connection, "CREATE TABLE musicvideo (idMVideo INTEGER PRIMARY KEY, idFile INTEGER, c00 TEXT)");
    }

    private static int InsertAndGetId(
        SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        Execute(connection, sql, parameters);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT last_insert_rowid()";
        return (int)(long)(command.ExecuteScalar() ?? 0L);
    }

    private static void Execute(
        SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }
}
