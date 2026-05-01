namespace MediaHandler.Application.Common.DTOs;

/// <summary>
///     Summarises the outcome of an automatic TMDB-matching pass over a collection
///     of unlinked <c>MediaFile</c> records.
/// </summary>
/// <param name="Matched">Number of files successfully matched and linked to a <c>Media</c> entity.</param>
/// <param name="Skipped">Number of files for which TMDB returned no usable result.</param>
/// <param name="Failed">Number of files that raised an exception during processing.</param>
/// <param name="Errors">Human-readable error messages collected from failed files.</param>
public record AutoMatchResult(
    int Matched,
    int Skipped,
    int Failed,
    IReadOnlyList<string> Errors);