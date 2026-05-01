using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MediaHandler.Application.Common.DTOs;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace MediaHandler.Infrastructure.Tmdb;

public sealed class TmdbService(HttpClient httpClient, ILogger<TmdbService> logger)
    : ITmdbService
{
    public async Task<TmdbMediaDto?> SearchMediaAsync(string query, string language,
        CancellationToken cancellationToken = default)
    {
        var url = $"/3/search/multi?query={Uri.EscapeDataString(query)}&language={language}";
        var response =
            await httpClient.GetFromJsonAsync<TmdbPagedResponse<TmdbSearchResultJson>>(url, cancellationToken);
        var first = response?.Results?.FirstOrDefault(r => r.MediaType is "movie" or "tv");
        if (first is null) return null;

        return new TmdbMediaDto(
            first.Id,
            first.Title ?? first.Name ?? string.Empty,
            first.OriginalTitle ?? first.OriginalName,
            first.Overview,
            first.MediaType ?? "unknown",
            ParseDate(first.ReleaseDate ?? first.FirstAirDate),
            first.PosterPath,
            first.BackdropPath,
            (decimal?)first.VoteAverage);
    }

    public async Task<TmdbMediaDetailsDto?> GetMediaDetailsAsync(int tmdbId, string mediaType, string language,
        CancellationToken cancellationToken = default)
    {
        var isMovie = mediaType.Equals("movie", StringComparison.OrdinalIgnoreCase);

        if (isMovie)
        {
            var url = $"/3/movie/{tmdbId}?language={language}";
            var movie = await httpClient.GetFromJsonAsync<TmdbMovieDetailsJson>(url, cancellationToken);
            if (movie is null) return null;

            return new TmdbMediaDetailsDto(
                movie.Id,
                movie.Title ?? string.Empty,
                movie.OriginalTitle,
                movie.Overview,
                "movie",
                ParseDate(movie.ReleaseDate),
                movie.Runtime,
                movie.PosterPath,
                movie.BackdropPath,
                (decimal?)movie.VoteAverage,
                movie.VoteCount,
                movie.Genres?.Select(g => g.Name).ToList(),
                movie.OriginalLanguage ?? "en");
        }
        else
        {
            var url = $"/3/tv/{tmdbId}?language={language}";
            var tv = await httpClient.GetFromJsonAsync<TmdbTvDetailsJson>(url, cancellationToken);
            if (tv is null) return null;

            return new TmdbMediaDetailsDto(
                tv.Id,
                tv.Name ?? string.Empty,
                tv.OriginalName,
                tv.Overview,
                "tv",
                ParseDate(tv.FirstAirDate),
                tv.EpisodeRunTime?.FirstOrDefault(),
                tv.PosterPath,
                tv.BackdropPath,
                (decimal?)tv.VoteAverage,
                tv.VoteCount,
                tv.Genres?.Select(g => g.Name).ToList(),
                tv.OriginalLanguage ?? "en");
        }
    }

    public async Task<IEnumerable<TmdbSeasonDto>> GetTvShowSeasonsAsync(int tmdbId, string language,
        CancellationToken cancellationToken = default)
    {
        var url = $"/3/tv/{tmdbId}?language={language}";
        var tv = await httpClient.GetFromJsonAsync<TmdbTvDetailsJson>(url, cancellationToken);
        if (tv?.Seasons is null) return [];

        var seasons = new List<TmdbSeasonDto>();
        foreach (var season in tv.Seasons)
        {
            var seasonUrl = $"/3/tv/{tmdbId}/season/{season.SeasonNumber}?language={language}";
            TmdbSeasonDetailsJson? details;
            try
            {
                details = await httpClient.GetFromJsonAsync<TmdbSeasonDetailsJson>(seasonUrl, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch season {SeasonNumber} for TMDB show {TmdbId}.",
                    season.SeasonNumber, tmdbId);
                continue;
            }

            if (details is null) continue;

            seasons.Add(new TmdbSeasonDto(
                details.SeasonNumber,
                details.Name ?? $"Season {details.SeasonNumber}",
                details.Overview,
                ParseDate(details.AirDate),
                details.PosterPath,
                details.Episodes?.Count ?? 0,
                details.Episodes?.Select(e => new TmdbEpisodeDto(
                    e.EpisodeNumber,
                    e.Name ?? $"Episode {e.EpisodeNumber}",
                    e.Overview,
                    ParseDate(e.AirDate),
                    e.StillPath,
                    e.Runtime)) ?? []));
        }

        return seasons;
    }

    // =========================================================================
    // Scanner: id-based lookups
    // =========================================================================

    /// <inheritdoc />
    public async Task<TmdbIdLookupResult?> GetMovieByIdAsync(
        int tmdbId, string language = "en-US", CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"/3/movie/{tmdbId}?language={language}";
            var movie = await httpClient.GetFromJsonAsync<TmdbMovieDetailsJson>(url, cancellationToken);
            if (movie is null) return null;

            return new TmdbIdLookupResult(
                movie.Id,
                MediaType.Film,
                movie.Title ?? string.Empty,
                ParseDate(movie.ReleaseDate)?.Year,
                movie.PosterPath);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "TMDB id-based movie lookup failed for id {TmdbId}.", tmdbId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<TmdbIdLookupResult?> GetTvShowByIdAsync(
        int tmdbId, string language = "en-US", CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"/3/tv/{tmdbId}?language={language}";
            var tv = await httpClient.GetFromJsonAsync<TmdbTvDetailsJson>(url, cancellationToken);
            if (tv is null) return null;

            return new TmdbIdLookupResult(
                tv.Id,
                MediaType.TvShow,
                tv.Name ?? string.Empty,
                ParseDate(tv.FirstAirDate)?.Year,
                tv.PosterPath);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "TMDB id-based TV show lookup failed for id {TmdbId}.", tmdbId);
            throw;
        }
    }

    // =========================================================================
    // Scanner: multi-candidate search
    // =========================================================================

    /// <inheritdoc />
    public async Task<IReadOnlyList<TmdbSearchCandidate>> SearchCandidatesAsync(
        string query,
        int? year,
        MediaType? kindHint,
        string language = "en-US",
        CancellationToken cancellationToken = default)
    {
        // Build the search URL — use /search/movie, /search/tv, or /search/multi based on kindHint
        string url;
        if (kindHint == MediaType.Film)
        {
            url = $"/3/search/movie?query={Uri.EscapeDataString(query)}&language={language}";
            if (year.HasValue) url += $"&year={year.Value}";
        }
        else if (kindHint == MediaType.TvShow)
        {
            url = $"/3/search/tv?query={Uri.EscapeDataString(query)}&language={language}";
            if (year.HasValue) url += $"&first_air_date_year={year.Value}";
        }
        else
        {
            // No hint: search both via /search/multi
            url = $"/3/search/multi?query={Uri.EscapeDataString(query)}&language={language}";
        }

        TmdbPagedResponse<TmdbSearchResultJson>? response;
        try
        {
            response = await httpClient.GetFromJsonAsync<TmdbPagedResponse<TmdbSearchResultJson>>(url,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "TMDB candidate search failed for query '{Query}'.", query);
            throw;
        }

        if (response?.Results is null) return [];

        // Convert to TmdbSearchCandidate, apply type filter, then take top 5 by popularity
        var candidates = response.Results
            .Where(r => r.MediaType is null or "movie" or "tv") // filter out "person"
            .Select(r => new TmdbSearchCandidate(
                r.Id,
                r.MediaType == "tv" ? MediaType.TvShow : MediaType.Film,
                r.Title ?? r.Name ?? string.Empty,
                ParseDate(r.ReleaseDate ?? r.FirstAirDate)?.Year,
                (decimal)(r.Popularity ?? r.VoteAverage ?? 0),
                r.PosterPath))
            .OrderByDescending(c => c.PopularityScore)
            .Take(5)
            .ToList();

        return candidates;
    }

    private static DateTime? ParseDate(string? date)
    {
        return DateTime.TryParse(date, out var result) ? result : null;
    }

    private record TmdbPagedResponse<T>(
        [property: JsonPropertyName("results")]
        List<T>? Results,
        [property: JsonPropertyName("total_results")]
        int TotalResults,
        [property: JsonPropertyName("total_pages")]
        int TotalPages);

    private record TmdbSearchResultJson(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("original_title")]
        string? OriginalTitle,
        [property: JsonPropertyName("original_name")]
        string? OriginalName,
        [property: JsonPropertyName("overview")]
        string? Overview,
        [property: JsonPropertyName("media_type")]
        string? MediaType,
        [property: JsonPropertyName("release_date")]
        string? ReleaseDate,
        [property: JsonPropertyName("first_air_date")]
        string? FirstAirDate,
        [property: JsonPropertyName("poster_path")]
        string? PosterPath,
        [property: JsonPropertyName("backdrop_path")]
        string? BackdropPath,
        [property: JsonPropertyName("vote_average")]
        double? VoteAverage,
        [property: JsonPropertyName("popularity")]
        double? Popularity);

    private record TmdbGenreJson(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("name")] string Name);

    private record TmdbMovieDetailsJson(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("original_title")]
        string? OriginalTitle,
        [property: JsonPropertyName("overview")]
        string? Overview,
        [property: JsonPropertyName("release_date")]
        string? ReleaseDate,
        [property: JsonPropertyName("runtime")]
        int? Runtime,
        [property: JsonPropertyName("poster_path")]
        string? PosterPath,
        [property: JsonPropertyName("backdrop_path")]
        string? BackdropPath,
        [property: JsonPropertyName("vote_average")]
        double? VoteAverage,
        [property: JsonPropertyName("vote_count")]
        int? VoteCount,
        [property: JsonPropertyName("genres")] List<TmdbGenreJson>? Genres,
        [property: JsonPropertyName("original_language")]
        string? OriginalLanguage);

    private record TmdbTvSeasonSummaryJson(
        [property: JsonPropertyName("season_number")]
        int SeasonNumber,
        [property: JsonPropertyName("name")] string? Name);

    private record TmdbTvDetailsJson(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("original_name")]
        string? OriginalName,
        [property: JsonPropertyName("overview")]
        string? Overview,
        [property: JsonPropertyName("first_air_date")]
        string? FirstAirDate,
        [property: JsonPropertyName("episode_run_time")]
        List<int>? EpisodeRunTime,
        [property: JsonPropertyName("poster_path")]
        string? PosterPath,
        [property: JsonPropertyName("backdrop_path")]
        string? BackdropPath,
        [property: JsonPropertyName("vote_average")]
        double? VoteAverage,
        [property: JsonPropertyName("vote_count")]
        int? VoteCount,
        [property: JsonPropertyName("genres")] List<TmdbGenreJson>? Genres,
        [property: JsonPropertyName("original_language")]
        string? OriginalLanguage,
        [property: JsonPropertyName("seasons")]
        List<TmdbTvSeasonSummaryJson>? Seasons);

    private record TmdbEpisodeJson(
        [property: JsonPropertyName("episode_number")]
        int EpisodeNumber,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("overview")]
        string? Overview,
        [property: JsonPropertyName("air_date")]
        string? AirDate,
        [property: JsonPropertyName("still_path")]
        string? StillPath,
        [property: JsonPropertyName("runtime")]
        int? Runtime);

    private record TmdbSeasonDetailsJson(
        [property: JsonPropertyName("season_number")]
        int SeasonNumber,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("overview")]
        string? Overview,
        [property: JsonPropertyName("air_date")]
        string? AirDate,
        [property: JsonPropertyName("poster_path")]
        string? PosterPath,
        [property: JsonPropertyName("episodes")]
        List<TmdbEpisodeJson>? Episodes);
}