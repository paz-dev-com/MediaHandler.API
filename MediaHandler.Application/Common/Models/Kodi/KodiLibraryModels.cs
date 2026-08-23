namespace MediaHandler.Application.Common.Models.Kodi;

/// <summary>
///     An external identifier attached to a Kodi library item (from the Kodi <c>uniqueid</c> table).
/// </summary>
/// <param name="Provider">Normalized provider name: <c>tmdb</c> | <c>imdb</c> | <c>tvdb</c> | other.</param>
/// <param name="Value">The identifier value at the provider site.</param>
public record KodiExternalId(string Provider, string Value);

/// <summary>
///     A Kodi movie row with its identity data and expanded file references.
/// </summary>
/// <param name="FileRefs">
///     The movie's file reference(s): a single <c>strPath + strFilename</c> URI, or every part
///     of a <c>stack://</c> reference expanded in part order.
/// </param>
public record KodiMovieItem(
    int KodiMovieId,
    string Title,
    string? OriginalTitle,
    int? Year,
    IReadOnlyList<KodiExternalId> ExternalIds,
    IReadOnlyList<string> FileRefs);

/// <summary>
///     A Kodi episode row. Multi-episode files surface as several items sharing the same
///     <see cref="FileRef" />.
/// </summary>
public record KodiEpisodeItem(
    int KodiEpisodeId,
    int SeasonNumber,
    int EpisodeNumber,
    string? Title,
    string FileRef);

/// <summary>
///     A Kodi TV-show row with its episodes (identity is resolved at show level only).
/// </summary>
public record KodiShowItem(
    int KodiShowId,
    string Title,
    int? Year,
    IReadOnlyList<KodiExternalId> ExternalIds,
    IReadOnlyList<KodiEpisodeItem> Episodes);

/// <summary>
///     A Kodi music-video row — never imported; surfaced for skip counting/reporting only.
/// </summary>
public record KodiMusicVideoItem(int KodiMusicVideoId, string Title);

/// <summary>
///     An in-memory snapshot of the Kodi video-database library relevant to the import.
/// </summary>
public record KodiLibrarySnapshot(
    IReadOnlyList<KodiMovieItem> Movies,
    IReadOnlyList<KodiShowItem> Shows,
    IReadOnlyList<KodiMusicVideoItem> MusicVideos);
