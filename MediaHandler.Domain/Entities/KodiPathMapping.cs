using MediaHandler.Domain.Common;

namespace MediaHandler.Domain.Entities;

/// <summary>
///     An ordered translation rule mapping a Kodi URI prefix (as seen by the Kodi box,
///     e.g. <c>smb://FREEBOX/Films/</c>) to an app NAS path prefix (e.g. <c>/nas/Movies/</c>).
/// </summary>
/// <remarks>
///     Prefixes are normalized on write (percent-decoded, separators unified, no trailing slash)
///     so that matching during import is a plain case-insensitive prefix test.
///     Evaluation order is <see cref="SortOrder" /> ascending (ties broken by creation time).
/// </remarks>
public class KodiPathMapping : BaseEntity
{
    /// <summary>Normalized Kodi URI prefix to match (unique).</summary>
    public required string KodiPrefix { get; set; }

    /// <summary>Normalized NAS path prefix the Kodi prefix rewrites to.</summary>
    public required string NasPrefix { get; set; }

    /// <summary>Evaluation order — lower values are evaluated first.</summary>
    public int SortOrder { get; set; }
}
