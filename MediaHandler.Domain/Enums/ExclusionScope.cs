namespace MediaHandler.Domain.Enums;

/// <summary>
/// Determines which part of a file-system entry an <c>ExclusionRule</c> pattern is matched against.
/// </summary>
public enum ExclusionScope
{
    /// <summary>Match against the bare filename (without directory segments).</summary>
    Filename,

    /// <summary>Match against any directory-name segment in the path.</summary>
    Folder,

    /// <summary>
    /// The pattern is the name of a marker file (e.g., <c>.nomedia</c>); if the file exists
    /// in a folder the entire subtree is excluded.
    /// </summary>
    MarkerFile,

    /// <summary>Match against the file extension (without the leading dot).</summary>
    Extension
}

