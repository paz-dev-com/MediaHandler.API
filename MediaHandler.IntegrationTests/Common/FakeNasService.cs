using MediaHandler.Application.Common.DTOs;
using MediaHandler.Application.Common.Interfaces;

namespace MediaHandler.IntegrationTests.Common;

/// <summary>
///     An in-memory implementation of <see cref="INasService" /> for use in scanner integration
///     tests. Eliminates any dependency on FreeboxNasService or a real NAS host.
///     Construct with a flat <see cref="NasFileInfo" /> list (typically produced by
///     <c>FixtureBuilder</c>) and inject via <see cref="ScannerIntegrationTestBase.WithFakeNasService" />.
/// </summary>
public sealed class FakeNasService : INasService
{
    private readonly IReadOnlyList<string> _configuredPaths;
    private readonly IReadOnlyList<NasFileInfo> _entries;

    /// <param name="entries">The complete flat enumeration of files and directories to serve.</param>
    /// <param name="configuredPaths">
    ///     Base paths to report via <see cref="GetConfiguredPathsAsync" />. Defaults to a single
    ///     "/nas" root when not specified.
    /// </param>
    public FakeNasService(
        IEnumerable<NasFileInfo> entries,
        IEnumerable<string>? configuredPaths = null)
    {
        _entries = entries.ToList().AsReadOnly();
        _configuredPaths = (configuredPaths?.ToList() ?? ["/nas"]).AsReadOnly();
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Returns all entries whose <see cref="NasFileInfo.FilePath" /> starts with
    ///     <paramref name="basePath" />. When <paramref name="basePath" /> is null or empty the full
    ///     in-memory list is returned.
    /// </remarks>
    public Task<IEnumerable<NasFileInfo>> ScanDirectoryAsync(
        string? basePath,
        CancellationToken cancellationToken = default)
    {
        var result = string.IsNullOrWhiteSpace(basePath)
            ? _entries
            : _entries.Where(e =>
                e.FilePath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetConfiguredPathsAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_configuredPaths);
    }

    /// <inheritdoc />
    public Task<bool> FileExistsAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            _entries.Any(e => string.Equals(e.FilePath, filePath, StringComparison.OrdinalIgnoreCase)));
    }

    /// <inheritdoc />
    public Task<NasFileInfo?> GetFileInfoAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            _entries.FirstOrDefault(e =>
                string.Equals(e.FilePath, filePath, StringComparison.OrdinalIgnoreCase)));
    }
}