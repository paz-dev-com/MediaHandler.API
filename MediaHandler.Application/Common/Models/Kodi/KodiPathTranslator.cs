using System.Text.RegularExpressions;

namespace MediaHandler.Application.Common.Models.Kodi;

/// <summary>Outcome kind of a Kodi URI → NAS path translation attempt.</summary>
public enum PathTranslationKind
{
    /// <summary>A mapping covered the Kodi prefix; the path was rewritten.</summary>
    Translated,

    /// <summary>No mapping covers the Kodi prefix.</summary>
    NoMapping,

    /// <summary>The URI uses a non-filesystem scheme (<c>pvr://</c>, <c>http://</c>, <c>upnp://</c>, …).</summary>
    UnsupportedScheme
}

/// <summary>
///     Result of translating one Kodi file URI.
///     <see cref="KodiDirectoryPrefix" /> carries the normalized Kodi directory portion of the URI
///     for report purposes when no mapping covers it.
/// </summary>
public record PathTranslation(PathTranslationKind Kind, string? TranslatedPath, string? KodiDirectoryPrefix);

/// <summary>
///     Pure translator between Kodi file URIs (<c>strPath + strFilename</c>, as seen by the Kodi
///     box) and the app's canonical NAS paths, driven by ordered admin-managed prefix mappings.
///     The same normalization is applied to mapping prefixes at write time, so matching is a
///     plain case-insensitive prefix test (scanner path-comparison convention).
/// </summary>
public static class KodiPathTranslator
{
    // Filesystem-accessible Kodi URI schemes; anything else (pvr, http, https, upnp, plugin, …)
    // can never map to a scanned NAS file.
    // SOURCE: Kodi wiki – Databases / observed Kodi strPath formats; non-filesystem protocols
    // per spec 008 §Edge Cases ("Non-file protocols").
    private static readonly HashSet<string> FilesystemSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "smb", "nfs", "file"
    };

    private static readonly Regex DuplicateSlashes = new(
        "/{2,}",
        RegexOptions.Compiled); // normalization utility — collapses duplicate separators

    /// <summary>
    ///     Translates <paramref name="kodiFileUri" /> through the ordered
    ///     <paramref name="mappings" /> (first match wins; per-upload overrides are prepended by
    ///     the caller, so they win on ties).
    /// </summary>
    public static PathTranslation Translate(string kodiFileUri, IReadOnlyList<KodiPathMappingSnapshot> mappings)
    {
        if (string.IsNullOrWhiteSpace(kodiFileUri))
            return new PathTranslation(PathTranslationKind.NoMapping, null, null);

        // 1. Scheme gate — scheme-less absolute paths proceed like filesystem URIs.
        var scheme = ExtractScheme(kodiFileUri);
        if (scheme is not null && !FilesystemSchemes.Contains(scheme))
            return new PathTranslation(PathTranslationKind.UnsupportedScheme, null, null);

        // 2. Normalize
        var normalized = Normalize(kodiFileUri);

        // 3+4. Match & rewrite — first mapping whose normalized KodiPrefix is an
        // OrdinalIgnoreCase prefix (on a '/' boundary) of the normalized URI wins.
        foreach (var mapping in mappings)
        {
            var prefix = mapping.KodiPrefix;
            if (prefix.Length == 0)
                continue;

            if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            if (normalized.Length > prefix.Length && normalized[prefix.Length] != '/')
                continue; // avoid "Films" matching "Films2/…"

            var remainder = normalized[prefix.Length..].TrimStart('/');
            var translated = remainder.Length == 0
                ? mapping.NasPrefix
                : mapping.NasPrefix.TrimEnd('/') + "/" + remainder;

            // Case-insensitivity is applied at match time by the caller (OrdinalIgnoreCase
            // dictionary over MediaFile.FilePath, scanner convention).
            return new PathTranslation(PathTranslationKind.Translated, translated, null);
        }

        // 5. No match — surface the normalized directory portion as the actionable prefix.
        return new PathTranslation(PathTranslationKind.NoMapping, null, DirectoryPrefixOf(normalized));
    }

    /// <summary>
    ///     Normalizes a Kodi URI or prefix: percent-decodes once, unifies separators to '/',
    ///     collapses duplicate slashes (preserving the <c>://</c> scheme separator), trims
    ///     trailing slashes.
    /// </summary>
    public static string Normalize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var decoded = Uri.UnescapeDataString(raw.Trim());
        var unified = decoded.Replace('\\', '/');

        var schemeSep = unified.IndexOf("://", StringComparison.Ordinal);
        var collapsed = schemeSep < 0
            ? DuplicateSlashes.Replace(unified, "/")
            : unified[..(schemeSep + 3)] + DuplicateSlashes.Replace(unified[(schemeSep + 3)..], "/");

        return collapsed.TrimEnd('/');
    }

    /// <summary>Normalization applied to mapping prefixes at write time (no trailing slash).</summary>
    public static string NormalizePrefix(string prefix)
    {
        return Normalize(prefix);
    }

    private static string? ExtractScheme(string uri)
    {
        var idx = uri.IndexOf("://", StringComparison.Ordinal);
        return idx < 0 ? null : uri[..idx];
    }

    private static string DirectoryPrefixOf(string normalizedUri)
    {
        var lastSlash = normalizedUri.LastIndexOf('/');
        return lastSlash <= 0 ? normalizedUri : normalizedUri[..lastSlash];
    }
}
