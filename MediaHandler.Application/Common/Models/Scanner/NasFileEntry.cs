namespace MediaHandler.Application.Common.Models.Scanner;

/// <summary>
///     A scanner-facing view of a single NAS filesystem entry.
///     Produced by <c>INasFileEnumerator</c> and consumed by every pipeline stage.
/// </summary>
public record NasFileEntry(
    /// <summary>Canonical absolute path on the NAS (e.g., <c>/nas/Movies/Inception (2010)/Inception.mkv</c>).</summary>
    string AbsolutePath,
    /// <summary>Filename with extension (no directory component).</summary>
    string FileName,
    /// <summary>Raw file size in bytes; 0 for directories.</summary>
    long SizeBytes,
    /// <summary>Last-modified timestamp from the NAS, in UTC.</summary>
    DateTime MtimeUtc,
    /// <summary>True when this entry represents a directory.</summary>
    bool IsDirectory,
    /// <summary>Lower-cased file extension without the leading dot; null for directories.</summary>
    string? Extension);