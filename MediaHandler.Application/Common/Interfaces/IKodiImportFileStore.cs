using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Common.Models.Kodi;

namespace MediaHandler.Application.Common.Interfaces;

/// <summary>
///     Temporary storage for uploaded Kodi database files.
///     Files are streamed to disk under a configurable size cap and discarded when the
///     owning run reaches a terminal state.
/// </summary>
public interface IKodiImportFileStore
{
    /// <summary>
    ///     Streams <paramref name="content" /> to a temporary file, enforcing the configured
    ///     size cap. Returns <c>Result.Fail</c> with <c>UPLOAD_TOO_LARGE</c> when the cap is
    ///     exceeded (the partial file is deleted). Never buffers the whole upload in memory.
    /// </summary>
    Task<Result<StoredUpload>> SaveAsync(Stream content, string fileName, long declaredLength, CancellationToken ct);

    /// <summary>Best-effort deletion of a previously stored upload; <c>null</c>-safe.</summary>
    void Delete(string? filePath);

    /// <summary>
    ///     Removes every file in the temp directory. Used by startup recovery — no legitimate
    ///     uploaded file can exist at startup.
    /// </summary>
    void PurgeOrphans();
}
