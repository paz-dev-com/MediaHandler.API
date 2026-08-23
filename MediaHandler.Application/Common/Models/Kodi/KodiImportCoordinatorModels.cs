using MediaHandler.Domain.Enums;

namespace MediaHandler.Application.Common.Models.Kodi;

/// <summary>
///     Parameters forwarded from the Application start command to the import coordinator.
/// </summary>
public record KodiImportStartParameters(
    Guid ImportRunId,
    string StoredFilePath,
    string SourceFileName,
    int SchemaVersion,
    KodiImportMode Mode,
    IReadOnlyList<KodiPathMappingSnapshot> Mappings);

/// <summary>
///     Lightweight handle returned by <c>IImportRunCoordinator.StartAsync</c>.
///     Callers poll the run row for progress and the final report.
/// </summary>
public record KodiImportRunHandle(Guid ImportRunId);

/// <summary>
///     A pre-normalized, ordered Kodi-prefix → NAS-prefix translation rule
///     (persisted mapping or per-upload override).
/// </summary>
public record KodiPathMappingSnapshot(string KodiPrefix, string NasPrefix);

/// <summary>
///     Result of storing an uploaded Kodi database file in temporary storage.
/// </summary>
public record StoredUpload(string FilePath, long SizeBytes);

/// <summary>
///     Result of validating an uploaded file as a Kodi video database.
///     <paramref name="ErrorCode" /> ∈ { <c>UNSUPPORTED_VERSION</c>, <c>INVALID_KODI_DB</c> }.
/// </summary>
public record KodiDbValidationResult(bool IsValid, string? ErrorCode, string? ErrorMessage)
{
    public static KodiDbValidationResult Valid()
    {
        return new KodiDbValidationResult(true, null, null);
    }

    public static KodiDbValidationResult Invalid(string errorCode, string errorMessage)
    {
        return new KodiDbValidationResult(false, errorCode, errorMessage);
    }
}
