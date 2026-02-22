namespace MediaHandler.Domain.Interfaces;

public interface INasService
{
    Task<IEnumerable<NasFileInfo>> ScanDirectoryAsync(string basePath, CancellationToken cancellationToken = default);
    Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default);
    Task<NasFileInfo?> GetFileInfoAsync(string filePath, CancellationToken cancellationToken = default);
}

public record NasFileInfo(
    string FilePath,
    string FileName,
    long SizeBytes,
    string? Format,
    DateTime CreatedAt,
    DateTime ModifiedAt);
