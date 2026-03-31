using MediaHandler.Application.Common.DTOs;

namespace MediaHandler.Application.Common.Interfaces;

public interface INasService
{
    /// <summary>Returns all entries (files AND directories) under the given path.
    /// When basePath is null or empty, scans all configured base paths.</summary>
    Task<IEnumerable<NasFileInfo>> ScanDirectoryAsync(string? basePath, CancellationToken cancellationToken = default);

    /// <summary>Returns the list of NAS base paths configured in appsettings.</summary>
    Task<IReadOnlyList<string>> GetConfiguredPathsAsync(CancellationToken cancellationToken = default);

    Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default);
    Task<NasFileInfo?> GetFileInfoAsync(string filePath, CancellationToken cancellationToken = default);
}
