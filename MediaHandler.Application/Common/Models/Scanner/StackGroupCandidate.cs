namespace MediaHandler.Application.Common.Models.Scanner;

/// <summary>
/// A single stacked-file group candidate returned by <c>IStackingDetector</c>.
/// All entries in <see cref="Parts"/> share the same base title and differ only
/// in their stacking suffix (cd1/cd2, part1/part2, disc1/disc2, etc.).
/// </summary>
public record StackGroupCandidate(
    /// <summary>Common base title shared by all parts (without the stacking suffix).</summary>
    string BaseTitle,
    /// <summary>Parent folder path that contains all part files.</summary>
    string FolderPath,
    /// <summary>The suffix discriminator detected (e.g., "cd", "part", "disc").</summary>
    string Discriminator,
    /// <summary>Ordered list of individual part files (by stacking ordinal).</summary>
    IReadOnlyList<NasFileEntry> Parts);

