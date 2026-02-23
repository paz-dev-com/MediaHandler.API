using MediaHandler.Application.Common.DTOs;

namespace MediaHandler.Application.Common.Interfaces;

public interface INasService
{
    Task<IEnumerable<NasFileInfo>> ScanDirectoryAsync(string basePath, CancellationToken cancellationToken = default);
    Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default);
    Task<NasFileInfo?> GetFileInfoAsync(string filePath, CancellationToken cancellationToken = default);
}
