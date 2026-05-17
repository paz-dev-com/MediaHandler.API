namespace MediaHandler.Application.Common;

/// <summary>
///     Shared constants for media file handling, used across Application and Infrastructure layers.
/// </summary>
public static class MediaFileConstants
{
    /// <summary>
    ///     The set of video file extensions that are recognised as importable media files.
    ///     Comparison is case-insensitive.
    /// </summary>
    public static readonly HashSet<string> VideoExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".mkv", ".mp4", ".avi", ".mov", ".wmv", ".m4v",
            ".flv", ".ts", ".m2ts", ".mpg", ".mpeg", ".webm"
        };

    /// <summary>
    ///     Returns <c>true</c> when the file at <paramref name="filePath" /> has a recognised
    ///     video extension; <c>false</c> otherwise.
    /// </summary>
    public static bool IsVideoFile(string filePath)
    {
        return VideoExtensions.Contains(Path.GetExtension(filePath));
    }
}