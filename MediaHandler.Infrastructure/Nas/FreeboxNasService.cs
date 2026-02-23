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

    public async Task<IEnumerable<NasFileInfo>> ScanDirectoryAsync(string basePath, CancellationToken cancellationToken = default)
    {
        if (_options.BasePaths.Count > 0 &&
            !_options.BasePaths.Any(bp => basePath.StartsWith(bp, StringComparison.OrdinalIgnoreCase)))
        {
            logger.LogWarning("Rejected scan request for path '{Path}': not within any configured base path.", basePath);
            throw new UnauthorizedAccessException("The requested path is not within the allowed base paths.");
        }

        var response = await GetFreeboxAsync<FreeboxResponse<List<FreeboxFileEntry>>>(
            $"/api/{_options.ApiVersion}/fs/ls/{EncodePath(basePath)}", cancellationToken);

        if (response?.Success != true || response.Result is null)
        {
            logger.LogWarning("Failed to scan directory {Path}: {Message}", basePath, response?.Msg);
            return [];
        }

        return response.Result.Where(e => e.Type == "file").Select(MapToNasFileInfo);
    }

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

        return new NasFileInfo(
            FilePath: entry.Path,
            FileName: entry.Name,
            SizeBytes: entry.Size ?? 0,
            Format: string.IsNullOrEmpty(extension) ? null : extension,
            CreatedAt: modified,
            ModifiedAt: modified);
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
