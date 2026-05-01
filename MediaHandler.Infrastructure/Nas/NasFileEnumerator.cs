using System.Runtime.CompilerServices;
using MediaHandler.Application.Common.DTOs;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models.Scanner;
using MediaHandler.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace MediaHandler.Infrastructure.Nas;

/// <summary>
///     Wraps <see cref="INasService" /> to yield <see cref="NasFileEntry" /> items as an
///     <see cref="IAsyncEnumerable{T}" /> consumed by the scanner pipeline.
/// </summary>
/// <remarks>
///     The underlying <c>INasService.ScanDirectoryAsync</c> returns a materialised list,
///     so this adapter simply projects and yields each item.  When the pipeline
///     architecture moves to a true streaming NAS client the adapter boundary stays intact.
/// </remarks>
public sealed class NasFileEnumerator(
    INasService nasService,
    ILogger<NasFileEnumerator> logger) : INasFileEnumerator
{
    public async IAsyncEnumerable<NasFileEntry> EnumerateAsync(
        LibraryRoot root,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        IEnumerable<NasFileInfo> entries;
        try
        {
            entries = await nasService.ScanDirectoryAsync(root.Path, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "NAS enumeration failed for library root {RootId} ({Path})", root.Id, root.Path);
            // Surface the failure to the caller; the pipeline handles NAS-unreachable errors.
            throw;
        }

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            var ext = string.IsNullOrEmpty(entry.Format)
                ? null
                : entry.Format.ToLowerInvariant();

            yield return new NasFileEntry(
                entry.FilePath,
                entry.FileName,
                entry.SizeBytes,
                entry.ModifiedAt.ToUniversalTime(),
                entry.IsDirectory,
                ext);
        }
    }
}