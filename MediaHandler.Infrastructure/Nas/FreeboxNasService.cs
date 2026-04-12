using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using MediaHandler.Application.Common.DTOs;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MediaHandler.Infrastructure.Nas;

public sealed class FreeboxNasService(
    IHttpClientFactory httpClientFactory,
    IOptions<NasOptions> options,
    ILogger<FreeboxNasService> logger) : INasService
{
    private readonly NasOptions _options = options.Value;
    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    private string? _sessionToken;

    public async Task<IEnumerable<NasFileInfo>> ScanDirectoryAsync(string? basePath, CancellationToken cancellationToken = default)
    {
        // When no path is given, scan every configured base path and aggregate
        if (string.IsNullOrWhiteSpace(basePath))
        {
            if (_options.BasePaths.Count == 0)
            {
                logger.LogWarning("No base paths configured for NAS scan.");
                return [];
            }

            var all = new List<NasFileInfo>();
            foreach (var bp in _options.BasePaths)
                all.AddRange(await ScanSinglePathAsync(bp, cancellationToken));
            return all;
        }

        // Guard: requested path must start with a configured base path
        if (_options.BasePaths.Count > 0 &&
            !_options.BasePaths.Any(bp => basePath.StartsWith(bp, StringComparison.OrdinalIgnoreCase)))
        {
            logger.LogWarning("Rejected scan request for path '{Path}': not within any configured base path.", basePath);
            throw new UnauthorizedAccessException("The requested path is not within the allowed base paths.");
        }

        return await ScanSinglePathAsync(basePath, cancellationToken);
    }

    public Task<IReadOnlyList<string>> GetConfiguredPathsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<string>>(_options.BasePaths.AsReadOnly());

    public async Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default) =>
        await GetFileInfoAsync(filePath, cancellationToken) is not null;

    public async Task<NasFileInfo?> GetFileInfoAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var response = await GetFreeboxAsync<FreeboxResponse<FreeboxFileEntry>>(
            $"/api/{_options.ApiVersion}/fs/info/{EncodePath(filePath)}", cancellationToken);

        return response?.Success == true && response.Result is not null
            ? MapToNasFileInfo(response.Result)
            : null;
    }

    private async Task<T?> GetFreeboxAsync<T>(string path, CancellationToken cancellationToken, bool isRetry = false)
    {
        var token = await EnsureSessionTokenAsync(cancellationToken);
        var client = httpClientFactory.CreateClient("Freebox");

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Fbx-App-Auth", token);

        var httpResponse = await client.SendAsync(request, cancellationToken);

        if (httpResponse.StatusCode == HttpStatusCode.Forbidden && !isRetry)
        {
            logger.LogInformation("Freebox session expired, re-authenticating.");
            InvalidateSession();
            return await GetFreeboxAsync<T>(path, cancellationToken, isRetry: true);
        }

        httpResponse.EnsureSuccessStatusCode();
        return await httpResponse.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
    }

    private async Task<string> EnsureSessionTokenAsync(CancellationToken cancellationToken)
    {
        if (_sessionToken is not null)
            return _sessionToken;

        await _sessionLock.WaitAsync(cancellationToken);
        try
        {
            if (_sessionToken is not null)
                return _sessionToken;

            var client = httpClientFactory.CreateClient("Freebox");

            var challengeResponse = await client.GetFromJsonAsync<FreeboxResponse<FreeboxChallenge>>(
                $"/api/{_options.ApiVersion}/login/", cancellationToken);

            if (challengeResponse?.Success != true || challengeResponse.Result is null)
                throw new InvalidOperationException("Failed to retrieve Freebox login challenge.");

            using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(_options.AppToken));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(challengeResponse.Result.Challenge));
            var password = Convert.ToHexString(hash).ToLower();

            var sessionResponse = await client.PostAsJsonAsync(
                $"/api/{_options.ApiVersion}/login/session/",
                new FreeboxSessionRequest(_options.AppId, password),
                cancellationToken);

            var session = await sessionResponse.Content
                .ReadFromJsonAsync<FreeboxResponse<FreeboxSessionResult>>(cancellationToken: cancellationToken);

            if (session?.Success != true || session.Result?.SessionToken is null)
                throw new InvalidOperationException("Failed to open Freebox session.");

            _sessionToken = session.Result.SessionToken;
            logger.LogInformation("Freebox session established successfully.");
            return _sessionToken;
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    private void InvalidateSession() => _sessionToken = null;

    private static string EncodePath(string path) =>
        WebUtility.UrlEncode(Convert.ToBase64String(Encoding.UTF8.GetBytes(path)));

    private static NasFileInfo MapToNasFileInfo(FreeboxFileEntry entry)
    {
        var modified = DateTimeOffset.FromUnixTimeSeconds(entry.Modification).UtcDateTime;
        var extension = Path.GetExtension(entry.Name).TrimStart('.').ToUpperInvariant();
        var isDirectory = entry.Type == "dir";

        // The Freebox API returns 'path' as a Base64-encoded UTF-8 string.
        // Decode it so that FilePath is always a human-readable plain-text path,
        // which can be safely re-encoded by EncodePath() for subsequent API calls.
        var plainPath = DecodeFreeboxPath(entry.Path);

        return new NasFileInfo(
            FilePath: plainPath,
            FileName: entry.Name,
            SizeBytes: entry.Size ?? 0,
            Format: isDirectory || string.IsNullOrEmpty(extension) ? null : extension,
            CreatedAt: modified,
            ModifiedAt: modified,
            IsDirectory: isDirectory);
    }

    /// <summary>
    /// Decodes a Freebox path from its Base64 representation to a plain-text UTF-8 string.
    /// Falls back to returning the input unchanged if it is not valid Base64.
    /// </summary>
    private static string DecodeFreeboxPath(string encodedPath)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(encodedPath));
        }
        catch
        {
            // Already plain text (e.g., in unit tests or future API versions)
            return encodedPath;
        }
    }

    private async Task<IEnumerable<NasFileInfo>> ScanSinglePathAsync(
        string path,
        CancellationToken cancellationToken,
        int depth = 0)
    {
        const int maxDepth = 10;

        if (depth > maxDepth)
        {
            logger.LogWarning(
                "Maximum scan depth ({MaxDepth}) reached at '{Path}'. Stopping recursion.", maxDepth, path);
            return [];
        }

        var response = await GetFreeboxAsync<FreeboxResponse<List<FreeboxFileEntry>>>(
            $"/api/{_options.ApiVersion}/fs/ls/{EncodePath(path)}", cancellationToken);

        if (response?.Success != true || response.Result is null)
        {
            logger.LogWarning("Failed to scan directory {Path}: {Message}", path, response?.Msg);
            return [];
        }

        var entries = response.Result.Select(MapToNasFileInfo).ToList();
        var result = new List<NasFileInfo>(entries);

        // Recurse into visible subdirectories (skip hidden dirs like .Recycle_Bin, .Spotlight-V100)
        foreach (var dir in entries.Where(e => e.IsDirectory && !e.FileName.StartsWith('.')))
        {
            logger.LogDebug("Recursing into subdirectory '{DirPath}' (depth={Depth}).", dir.FilePath, depth + 1);
            var subEntries = await ScanSinglePathAsync(dir.FilePath, cancellationToken, depth + 1);
            result.AddRange(subEntries);
        }

        return result;
    }

    private record FreeboxResponse<T>(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("msg")] string? Msg,
        [property: JsonPropertyName("error_code")] string? ErrorCode,
        [property: JsonPropertyName("result")] T? Result);

    private record FreeboxChallenge(
        [property: JsonPropertyName("challenge")] string Challenge,
        [property: JsonPropertyName("logged_in")] bool LoggedIn);

    private record FreeboxSessionRequest(
        [property: JsonPropertyName("app_id")] string AppId,
        [property: JsonPropertyName("password")] string Password);

    private record FreeboxSessionResult(
        [property: JsonPropertyName("session_token")] string? SessionToken);

    private record FreeboxFileEntry(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("size")] long? Size,
        [property: JsonPropertyName("mimetype")] string? Mimetype,
        [property: JsonPropertyName("modification")] long Modification);
}
