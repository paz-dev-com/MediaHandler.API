using System.ComponentModel.DataAnnotations;

namespace MediaHandler.Infrastructure.Options;

/// <summary>
///     Configuration for the Kodi video-database import feature (section <c>"KodiImport"</c>).
/// </summary>
public class KodiImportOptions
{
    public const string Section = "KodiImport";

    /// <summary>
    ///     Maximum accepted upload size. The default is 100 MB — typical video databases
    ///     are 1–50 MB. Hard ceiling 500 MB matches the transport-level request size limit.
    /// </summary>
    [Range(1_048_576, 524_288_000)]
    public long MaxUploadSizeBytes { get; set; } = 404_857_600;

    /// <summary>
    ///     Supported Kodi video-database schema versions (file name suffix of
    ///     <c>MyVideos&lt;version&gt;.db</c>): 119 = Kodi 19, 121 = Kodi 20, 131 = Kodi 21.
    /// </summary>
    public int[] SupportedSchemaVersions { get; set; } = [119, 121, 131];

    /// <summary>
    ///     Directory uploaded database files are streamed to while a run processes them.
    ///     Defaults to <c>&lt;temp&gt;/mediahandler/kodi-imports</c>.
    /// </summary>
    public string? TempDirectory { get; set; }

    /// <summary>The effective temp directory (configured value or the default).</summary>
    public string EffectiveTempDirectory =>
        string.IsNullOrWhiteSpace(TempDirectory)
            ? Path.Combine(Path.GetTempPath(), "mediahandler", "kodi-imports")
            : TempDirectory;
}
