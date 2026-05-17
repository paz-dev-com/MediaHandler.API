namespace MediaHandler.Application.Common.Models.Scanner;

/// <summary>
///     Runtime-configurable release tag patterns bound from the <c>Scanner:ReleaseTags</c>
///     configuration section via <c>IOptionsMonitor&lt;ReleaseTagOptions&gt;</c>.
///     Patterns are merged with the built-in defaults in <c>KodiNameParser</c>
///     and applied during the title-cleaning pipeline to strip release-group tags,
///     quality identifiers, codecs, sources, and language markers that appear before
///     the SxxExx marker in TV episode filenames.
/// </summary>
public sealed class ReleaseTagOptions
{
    /// <summary>
    ///     Configuration section name used when binding via <c>IConfiguration</c>.
    /// </summary>
    public const string SectionName = "Scanner:ReleaseTags";

    /// <summary>
    ///     Additional regex patterns (case-insensitive) to strip from the pre-SxxExx
    ///     portion of a TV episode filename, supplementing the built-in defaults.
    ///     Each entry is a raw regex pattern string; the caller wraps it in word-boundary
    ///     or positional anchors as appropriate.
    ///     Example: <c>["AMZN", "DSNP", "NF"]</c> to handle streaming-source tags
    ///     not covered by the built-in list.
    /// </summary>
    public IReadOnlyList<string> AdditionalPatterns { get; set; } = [];
}

