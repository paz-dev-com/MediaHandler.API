using MediaHandler.Application.Common.Models.Scanner;

namespace MediaHandler.Application.Common.Interfaces;

/// <summary>
///     Detects stacked multi-part movie files within a folder using Kodi-compatible
///     stacking suffixes (cd1/cd2, part1/part2, disc1/disc2, (a)/(b), pt1/pt2).
/// </summary>
public interface IStackingDetector
{
    /// <summary>
    ///     Groups the provided file entries into stacked-movie candidates.
    ///     Files that do not form a stack are not included in any returned group.
    /// </summary>
    /// <param name="filesInFolder">
    ///     All video files within a single folder (already filtered through the exclusion evaluator).
    /// </param>
    /// <returns>One <see cref="StackGroupCandidate" /> per detected stack group.</returns>
    IReadOnlyList<StackGroupCandidate> Group(IEnumerable<NasFileEntry> filesInFolder);
}