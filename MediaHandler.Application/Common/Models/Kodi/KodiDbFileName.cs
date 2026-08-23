using System.Text.RegularExpressions;

namespace MediaHandler.Application.Common.Models.Kodi;

/// <summary>
///     Parses the Kodi video-database file name (<c>MyVideos&lt;version&gt;.db</c>) to recover the
///     schema version it carries.
/// </summary>
public static class KodiDbFileName
{
    // SOURCE: Kodi wiki – Databases (video DB file naming: MyVideos<version>.db in userdata/Database)
    private static readonly Regex VersionPattern = new(
        @"^MyVideos(?<v>\d+)\.db$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    ///     Attempts to extract the schema version from <paramref name="fileName" />.
    ///     Returns <c>false</c> for any name that does not match the documented pattern
    ///     (covers <c>MyMusic*.db</c> uploads and browser-renamed copies).
    /// </summary>
    public static bool TryParseVersion(string? fileName, out int version)
    {
        version = 0;
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        var match = VersionPattern.Match(Path.GetFileName(fileName.Trim()));
        if (!match.Success)
            return false;

        return int.TryParse(match.Groups["v"].Value, out version);
    }
}
