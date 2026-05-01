// ExclusionEvaluator — clean-room implementation of Kodi-equivalent file/folder exclusion rules.
//
// R-001 CLEAN-ROOM DECLARATION
// All exclusion rules sourced from:
//   https://kodi.wiki/view/Advancedsettings.xml (videoextensions, exclusion patterns)
//   https://kodi.wiki/view/Naming_video_files (sample/extras conventions)
//   Observed black-box Kodi behaviour (no GPL source consulted).

using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models.Scanner;

namespace MediaHandler.Infrastructure.Nas.Scanner;

/// <summary>
///     Evaluates each NAS entry against the configured exclusion rules.
///     Returns an <see cref="ExclusionVerdict" /> for every entry.
/// </summary>
public sealed class ExclusionEvaluator : IExclusionEvaluator
{
    public ExclusionVerdict Evaluate(NasFileEntry entry, ExclusionContext ctx)
    {
        // ── Directories are never media files ────────────────────────────────
        // SOURCE: Observed Kodi behaviour — pipeline treats directories as containers
        if (entry.IsDirectory)
            return new ExclusionVerdict(true, "Not a media file (directory)", "not-a-file");

        // ── .nomedia file itself ──────────────────────────────────────────────
        // SOURCE: Kodi advancedsettings — ".nomedia" file presence suppresses the folder
        if (string.Equals(entry.FileName, ".nomedia", StringComparison.OrdinalIgnoreCase))
            return new ExclusionVerdict(true, ".nomedia marker file", "nomedia-marker");

        // ── .nomedia subtree ─────────────────────────────────────────────────
        // SOURCE: Kodi advancedsettings — any file under a .nomedia-marked folder is excluded
        if (ctx.NomediaFolders is { Count: > 0 })
            foreach (var markedFolder in ctx.NomediaFolders)
                if (entry.AbsolutePath.StartsWith(markedFolder, StringComparison.OrdinalIgnoreCase)
                    && entry.AbsolutePath.Length > markedFolder.Length)
                    return new ExclusionVerdict(true, $"Under .nomedia folder: {markedFolder}", "nomedia-subtree");

        // ── Hidden folder (Unix dot-prefix) ──────────────────────────────────
        // SOURCE: Observed Kodi behaviour — directories whose names start with '.' are skipped
        var normalised = entry.AbsolutePath.Replace('\\', '/');
        var pathSegments = normalised.Split('/');
        foreach (var segment in pathSegments[..^1]) // all directory segments, not the filename
            if (segment.StartsWith('.') && segment.Length > 1)
                return new ExclusionVerdict(true, $"Hidden folder: {segment}", "hidden-folder");

        // ── Excluded folder names (Extras, Sample, Featurettes, etc.) ────────
        // SOURCE: Kodi wiki — these subfolder names are excluded from media scanning
        foreach (var segment in pathSegments[..^1])
        {
            if (KodiRegexCatalog.ExcludedFolderNames.Contains(segment))
            {
                var ruleId = GetFolderRuleId(segment);
                return new ExclusionVerdict(true, $"Excluded subfolder: {segment}", ruleId);
            }

            // Behind the Scenes (multi-word)
            if (segment.Equals("behind the scenes", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("behind-the-scenes", StringComparison.OrdinalIgnoreCase))
                return new ExclusionVerdict(true, $"Excluded subfolder: {segment}", "behind-the-scenes-folder");

            if (segment.Equals("deleted scenes", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("deleted-scenes", StringComparison.OrdinalIgnoreCase))
                return new ExclusionVerdict(true, $"Excluded subfolder: {segment}", "deleted-scenes-folder");
        }

        // ── Video extension allow-list ────────────────────────────────────────
        // SOURCE: Kodi wiki advancedsettings <videoextensions>
        if (string.IsNullOrEmpty(entry.Extension)
            || !KodiRegexCatalog.VideoExtensions.Contains(entry.Extension))
            return new ExclusionVerdict(true, $"Non-video extension: .{entry.Extension ?? "(none)"}",
                "non-video-extension");

        // ── Sample filename pattern ───────────────────────────────────────────
        // SOURCE: Kodi wiki — "Files with '-sample' suffix are excluded"
        var nameNoExt = Path.GetFileNameWithoutExtension(entry.FileName);
        if (KodiRegexCatalog.SampleFilenamePattern.IsMatch(nameNoExt))
            return new ExclusionVerdict(true, "Sample file", "sample-filename");

        // SOURCE: Kodi advancedsettings <trailerextensions> — trailer keyword in filename
        if (KodiRegexCatalog.TrailerFilenamePattern.IsMatch(nameNoExt))
            return new ExclusionVerdict(true, "Trailer file", "trailer-filename");

        // ── Passed all checks ─────────────────────────────────────────────────
        return new ExclusionVerdict(false);
    }

    private static string GetFolderRuleId(string segment)
    {
        return segment.ToLowerInvariant() switch
        {
            "sample" => "sample-folder",
            "extras" => "extras-folder",
            "featurettes" or "featurette" => "featurettes-folder",
            "trailers" => "trailers-folder",
            "shorts" => "shorts-folder",
            "scenes" => "scenes-folder",
            "interviews" => "interviews-folder",
            "behind the scenes" or "behind-the-scenes" => "behind-the-scenes-folder",
            "deleted scenes" or "deleted-scenes" => "deleted-scenes-folder",
            _ => "extras-folder"
        };
    }
}