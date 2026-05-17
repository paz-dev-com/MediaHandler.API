namespace MediaHandler.Application.Common.DTOs;

/// <summary>
///     Structured media metadata extracted from a NAS file path.
/// </summary>
/// <param name="Title">
///     The human-readable title extracted from the filename or parent folder
///     (e.g., <c>"The Matrix"</c>).
/// </param>
/// <param name="Year">
///     The release year when detected in the path (e.g., <c>1999</c>),
///     or <c>null</c> when absent.
/// </param>
/// <param name="MediaTypeHint">
///     A canonical TMDB media-type string inferred from path segments:
///     <c>"movie"</c>, <c>"tv"</c>, or <c>null</c> when undetermined.
/// </param>
public record ParsedMediaInfo(
    string Title,
    int? Year,
    string? MediaTypeHint);