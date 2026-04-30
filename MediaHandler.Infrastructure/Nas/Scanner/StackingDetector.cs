#nullable enable
// StackingDetector — clean-room implementation of Kodi multi-part movie stacking detection.
//
// R-001 CLEAN-ROOM DECLARATION
// Stacking rules sourced from:
//   https://kodi.wiki/view/Advancedsettings.xml#stackingregex
//   https://kodi.wiki/view/Naming_video_files/Movies (stacked movies section)
// No GPL source from /home/tpfeifer/Repos/xbmc-master/ was consulted.

using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models.Scanner;

namespace MediaHandler.Infrastructure.Nas.Scanner;

/// <summary>
/// Detects stacked multi-part movie files using Kodi-equivalent suffix patterns.
/// <para>
/// SOURCE: Kodi wiki advancedsettings <c>stackingregex</c> — recognised suffixes are:
/// cd1/cd2, disc1/disc2, disk1/disk2, part1/part2, pt1/pt2, (a)/(b)...(e).
/// </para>
/// </summary>
public sealed class StackingDetector : IStackingDetector
{
    public IReadOnlyList<StackGroupCandidate> Group(IEnumerable<NasFileEntry> filesInFolder)
    {
        var files = filesInFolder as IList<NasFileEntry> ?? filesInFolder.ToList();
        if (files.Count < 2)
            return [];

        // Group files by folder path first
        var byFolder = files
            .Where(f => !f.IsDirectory)
            .GroupBy(f => System.IO.Path.GetDirectoryName(f.AbsolutePath) ?? string.Empty);

        var results = new List<StackGroupCandidate>();

        foreach (var folderGroup in byFolder)
        {
            var folderPath = folderGroup.Key;
            var folderFiles = folderGroup.ToList();

            // Try each stacking pattern family
            foreach (var (discriminator, pattern) in KodiRegexCatalog.AllStackPatterns)
            {
                var candidates = new Dictionary<string, List<(NasFileEntry Entry, int Ordinal)>>(
                    StringComparer.OrdinalIgnoreCase);

                foreach (var file in folderFiles)
                {
                    var nameNoExt = System.IO.Path.GetFileNameWithoutExtension(file.FileName);
                    var m = pattern.Match(nameNoExt);
                    if (!m.Success) continue;

                    // The base name is the filename with the stacking suffix removed
                    var baseName = nameNoExt[..m.Index].TrimEnd('.', ' ', '_', '-');
                    if (string.IsNullOrWhiteSpace(baseName)) continue;

                    var ordinalStr = m.Groups[1].Value;
                    // For letter-based stacking "(a)"/"(b)" convert to ordinal
                    var ordinal = discriminator == "()"
                        ? ordinalStr.ToLowerInvariant()[0] - 'a' + 1
                        : int.TryParse(ordinalStr, out var n) ? n : 0;

                    if (!candidates.TryGetValue(baseName, out var list))
                        candidates[baseName] = list = [];

                    list.Add((file, ordinal));
                }

                foreach (var (baseName, parts) in candidates)
                {
                    // SOURCE: Kodi wiki — a stack requires ≥ 2 parts
                    if (parts.Count < 2) continue;

                    var ordered = parts
                        .OrderBy(p => p.Ordinal)
                        .Select(p => p.Entry)
                        .ToList()
                        .AsReadOnly();

                    results.Add(new StackGroupCandidate(
                        BaseTitle: baseName,
                        FolderPath: folderPath,
                        Discriminator: discriminator.TrimStart('(').TrimEnd(')'),
                        Parts: ordered));
                }
            }
        }

        return results;
    }
}

