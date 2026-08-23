using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Common.Models.Kodi;
using MediaHandler.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MediaHandler.Infrastructure.Services;

/// <summary>
///     Streams uploaded Kodi database files to a temp directory under a configurable size cap.
///     The upload is treated as untrusted input: it is never loaded fully into memory and the
///     stored file is opened read-only by the reader.
/// </summary>
public sealed class KodiImportFileStore(
    IOptions<KodiImportOptions> options,
    ILogger<KodiImportFileStore> logger) : IKodiImportFileStore
{
    private const int BufferSize = 80 * 1024; // 80 KB streaming chunks

    /// <inheritdoc />
    public async Task<Result<StoredUpload>> SaveAsync(
        Stream content, string fileName, long declaredLength, CancellationToken ct)
    {
        var maxBytes = options.Value.MaxUploadSizeBytes;

        if (declaredLength > maxBytes)
            return Result.Fail<StoredUpload>(TooLargeMessage(maxBytes));

        var directory = options.Value.EffectiveTempDirectory;
        Directory.CreateDirectory(directory);

        var filePath = Path.Combine(directory, $"kodi-import-{Guid.NewGuid()}.db");

        try
        {
            await using var output = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var buffer = new byte[BufferSize];
            long total = 0;

            while (true)
            {
                var read = await content.ReadAsync(buffer, ct);
                if (read == 0)
                    break;

                total += read;
                if (total > maxBytes)
                {
                    output.Close();
                    File.Delete(filePath);
                    return Result.Fail<StoredUpload>(TooLargeMessage(maxBytes));
                }

                await output.WriteAsync(buffer.AsMemory(0, read), ct);
            }

            logger.LogDebug("Stored Kodi upload '{FileName}' ({Size} bytes) at {FilePath}.",
                fileName, total, filePath);

            return Result.Success(new StoredUpload(filePath, total));
        }
        catch
        {
            Delete(filePath);
            throw;
        }
    }

    /// <inheritdoc />
    public void Delete(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return;

        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete Kodi upload temp file {FilePath}.", filePath);
        }
    }

    /// <inheritdoc />
    public void PurgeOrphans()
    {
        var directory = options.Value.EffectiveTempDirectory;

        try
        {
            if (!Directory.Exists(directory))
                return;

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to purge orphaned Kodi upload {FilePath}.", file);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to enumerate Kodi upload temp directory {Directory}.", directory);
        }
    }

    private static string TooLargeMessage(long maxBytes)
    {
        return $"UPLOAD_TOO_LARGE: The uploaded file exceeds the configured size limit of {maxBytes / 1_048_576} MB.";
    }
}
