using MediaHandler.Application.Common.Models.Scanner;
using MediaHandler.Domain.Entities;

namespace MediaHandler.Application.Common.Interfaces;

/// <summary>
///     Enumerates NAS filesystem entries under a configured <see cref="LibraryRoot" />
///     as an asynchronous stream, delegating the underlying I/O to <see cref="INasService" />.
/// </summary>
public interface INasFileEnumerator
{
    /// <summary>
    ///     Asynchronously yields every <see cref="NasFileEntry" /> (files and directories)
    ///     found recursively under <paramref name="root" />.
    ///     The enumeration is lazy: the caller can cancel at any time via <paramref name="ct" />.
    /// </summary>
    /// <param name="root">The library root to enumerate.</param>
    /// <param name="ct">Cancellation token propagated from the scan run.</param>
    IAsyncEnumerable<NasFileEntry> EnumerateAsync(LibraryRoot root, CancellationToken ct = default);
}