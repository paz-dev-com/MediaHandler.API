using MediaHandler.Domain.Enums;

namespace MediaHandler.Application.Features.KodiImport.DTOs;

/// <summary>Denormalized summary counters of an import run (FR-026).</summary>
public record ImportCountsDto(
    int TotalItems,
    int MoviesCreated,
    int ShowsCreated,
    int EpisodesCreated,
    int ItemsReused,
    int ItemsUnchanged,
    int FilesLinked,
    int UnmatchedPaths,
    int NoScannedFiles,
    int UnsupportedLocations,
    int Conflicts,
    int NoLongerInKodi,
    int NeedsReview,
    int IdentityLookupFailures,
    int SkippedMusicVideos);

/// <summary>Summary view of an import run (history list, active run, start response).</summary>
public record ImportRunDto(
    Guid Id,
    KodiImportMode Mode,
    ImportRunStatus Status,
    string SourceFileName,
    int SchemaVersion,
    DateTime StartedAt,
    DateTime? FinishedAt,
    string? FailureReason,
    ImportCountsDto Counts);

/// <summary>Detail view of an import run, including the uncovered Kodi path prefixes.</summary>
public record ImportRunDetailDto(
    Guid Id,
    KodiImportMode Mode,
    ImportRunStatus Status,
    string SourceFileName,
    int SchemaVersion,
    DateTime StartedAt,
    DateTime? FinishedAt,
    string? FailureReason,
    ImportCountsDto Counts,
    IReadOnlyList<string> UnmatchedPrefixes);

/// <summary>Per-Kodi-item outcome row of an import run.</summary>
public record ImportItemOutcomeDto(
    Guid Id,
    KodiItemKind ItemKind,
    int KodiItemId,
    string Title,
    MediaType? MediaKind,
    ImportItemStatus Outcome,
    ImportLinkStatus? LinkOutcome,
    int LinkedFileCount,
    string? Reason,
    string? KodiPathPrefix,
    Guid? MediaId);

/// <summary>An ordered Kodi-prefix → NAS-prefix translation rule.</summary>
public record KodiPathMappingDto(
    Guid Id,
    string KodiPrefix,
    string NasPrefix,
    int SortOrder);

/// <summary>Manual mappings between <c>ImportRun</c> rows and their DTOs (JSON fields deserialized).</summary>
public static class ImportRunMappings
{
    public static ImportCountsDto ToCounts(Domain.Entities.ImportRun run)
    {
        return new ImportCountsDto(
            run.TotalItems,
            run.MoviesCreated,
            run.ShowsCreated,
            run.EpisodesCreated,
            run.ItemsReused,
            run.ItemsUnchanged,
            run.FilesLinked,
            run.UnmatchedPaths,
            run.NoScannedFiles,
            run.UnsupportedLocations,
            run.Conflicts,
            run.NoLongerInKodi,
            run.NeedsReview,
            run.IdentityLookupFailures,
            run.SkippedMusicVideos);
    }

    public static ImportRunDto ToDto(Domain.Entities.ImportRun run)
    {
        return new ImportRunDto(
            run.Id,
            run.Mode,
            run.Status,
            run.SourceFileName,
            run.SchemaVersion,
            run.StartedAt,
            run.FinishedAt,
            run.FailureReason,
            ToCounts(run));
    }

    public static ImportRunDetailDto ToDetailDto(Domain.Entities.ImportRun run)
    {
        var prefixes = System.Text.Json.JsonSerializer.Deserialize<List<string>>(run.UnmatchedPrefixesJson) ?? [];
        return new ImportRunDetailDto(
            run.Id,
            run.Mode,
            run.Status,
            run.SourceFileName,
            run.SchemaVersion,
            run.StartedAt,
            run.FinishedAt,
            run.FailureReason,
            ToCounts(run),
            prefixes);
    }
}
