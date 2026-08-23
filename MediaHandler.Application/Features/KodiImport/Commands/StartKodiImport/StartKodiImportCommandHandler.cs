using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Common.Models.Kodi;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.KodiImport.Commands.StartKodiImport;

/// <summary>
///     Uploads a Kodi video database and starts a background import (or preview) run.
///     <paramref name="Content" /> is the raw upload stream; it is disposed by the caller.
/// </summary>
public record StartKodiImportCommand(
    string FileName,
    long DeclaredLengthBytes,
    Stream Content,
    KodiImportMode Mode,
    IReadOnlyList<KodiPathMappingSnapshot>? Overrides) : IRequest<Result<KodiImportRunHandle>>;

public sealed class StartKodiImportCommandHandler(
    IApplicationDbContext db,
    IKodiVideoDbReader reader,
    IKodiImportFileStore fileStore,
    IImportRunCoordinator coordinator)
    : IRequestHandler<StartKodiImportCommand, Result<KodiImportRunHandle>>
{
    public async Task<Result<KodiImportRunHandle>> Handle(
        StartKodiImportCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Schema version comes from the file name suffix (FR-003)
        if (!KodiDbFileName.TryParseVersion(request.FileName, out var schemaVersion))
        {
            return Result.Fail<KodiImportRunHandle>(
                "INVALID_FILE_NAME: The uploaded file name does not look like a Kodi video database. " +
                "Keep the original MyVideos<version>.db file name (e.g. MyVideos121.db).");
        }

        // 2. Store the upload (size cap enforced inside, streamed — FR-002)
        var stored = await fileStore.SaveAsync(
            request.Content, request.FileName, request.DeclaredLengthBytes, cancellationToken);
        if (!stored.IsSuccess)
            return Result.Fail<KodiImportRunHandle>(stored.Errors);

        var storedFilePath = stored.Value.FilePath;

        // 3. Validate the file as a Kodi video database — on any failure nothing persists
        //    and the upload is discarded (FR-004).
        var validation = await reader.ValidateAsync(storedFilePath, schemaVersion, cancellationToken);
        if (!validation.IsValid)
        {
            fileStore.Delete(storedFilePath);
            return Result.Fail<KodiImportRunHandle>(
                $"{validation.ErrorCode}: {validation.ErrorMessage}");
        }

        // 4. Effective mappings: normalized per-upload overrides first, then persisted mappings
        //    whose KodiPrefix is not shadowed by an override.
        var mappings = await LoadEffectiveMappingsAsync(request.Overrides, cancellationToken);

        // 5. Single-active-import guard
        var activeRun = await db.ImportRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Status == ImportRunStatus.Running, cancellationToken);
        if (activeRun is not null)
        {
            fileStore.Delete(storedFilePath);
            return Result.Fail<KodiImportRunHandle>(
                "IMPORT_IN_PROGRESS: An import is already running. Wait for it to complete.");
        }

        // 6. Delegate to the coordinator (owns the run row + background execution)
        try
        {
            var handle = await coordinator.StartAsync(
                new KodiImportStartParameters(
                    Guid.NewGuid(),
                    storedFilePath,
                    Path.GetFileName(request.FileName),
                    schemaVersion,
                    request.Mode,
                    mappings),
                cancellationToken);

            return Result.Success(handle);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("IMPORT_IN_PROGRESS"))
        {
            fileStore.Delete(storedFilePath);
            return Result.Fail<KodiImportRunHandle>(
                "IMPORT_IN_PROGRESS: An import is already running. Wait for it to complete.");
        }
    }

    private async Task<IReadOnlyList<KodiPathMappingSnapshot>> LoadEffectiveMappingsAsync(
        IReadOnlyList<KodiPathMappingSnapshot>? overrides,
        CancellationToken ct)
    {
        var effective = new List<KodiPathMappingSnapshot>();
        var overridePrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (overrides is not null)
        {
            foreach (var mappingOverride in overrides)
            {
                var normalized = new KodiPathMappingSnapshot(
                    KodiPathTranslator.NormalizePrefix(mappingOverride.KodiPrefix),
                    KodiPathTranslator.NormalizePrefix(mappingOverride.NasPrefix));
                effective.Add(normalized);
                overridePrefixes.Add(normalized.KodiPrefix);
            }
        }

        var persisted = await db.KodiPathMappings
            .AsNoTracking()
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.CreatedAt)
            .ToListAsync(ct);

        foreach (var mapping in persisted)
        {
            if (overridePrefixes.Contains(mapping.KodiPrefix))
                continue;
            effective.Add(new KodiPathMappingSnapshot(mapping.KodiPrefix, mapping.NasPrefix));
        }

        return effective;
    }
}
