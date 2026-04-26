using MediaHandler.Application.Common;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaHandler.Application.Features.Files.Commands.ScanAndImportNas;

/// <summary>
/// Orchestrates a full scan-and-import pipeline:
/// (1) scans the NAS for new files, (2) persists newly discovered <c>MediaFile</c> records,
/// (3) delegates all unlinked files to <see cref="IMediaAutoMatchService"/> for TMDB matching,
/// and (4) returns aggregated scan + match statistics.
/// The operation is idempotent: re-running it will not create duplicate <c>MediaFile</c>
/// or <c>Media</c> records.
/// </summary>
public class ScanAndImportNasCommandHandler(
    IApplicationDbContext context,
    INasService nasService,
    IMediaAutoMatchService autoMatchService,
    ILogger<ScanAndImportNasCommandHandler> logger)
    : IRequestHandler<ScanAndImportNasCommand, Result<ScanAndImportNasResult>>
{
    public async Task<Result<ScanAndImportNasResult>> Handle(
        ScanAndImportNasCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Scan the NAS
        logger.LogInformation(
            "Starting NAS scan. BasePath={BasePath}", request.BasePath ?? "(all)");

        var entries = (await nasService.ScanDirectoryAsync(request.BasePath, cancellationToken)).ToList();

        var foldersFound = entries.Count(e => e.IsDirectory);
        var allFiles = entries.Where(e => !e.IsDirectory).ToList();

        // Only retain recognized video files — skip system files, PDFs, executables, etc.
        var files = allFiles
            .Where(e => MediaFileConstants.IsVideoFile(e.FileName))
            .ToList();

        var skippedNonMedia = allFiles.Count - files.Count;
        if (skippedNonMedia > 0)
            logger.LogInformation(
                "Ignored {SkippedNonMedia} non-video file(s) returned by the NAS scan.",
                skippedNonMedia);

        logger.LogInformation(
            "NAS scan complete. VideoFiles={FileCount}, Folders={FolderCount}.",
            files.Count, foldersFound);

        // 2. Dedup by FilePath — only add genuinely new files
        var existingPaths = await context.MediaFiles
            .Select(mf => mf.FilePath)
            .ToHashSetAsync(cancellationToken);

        var newFiles = 0;
        foreach (var file in files.Where(file => !existingPaths.Contains(file.FilePath)))
        {
            // Legacy handler — placeholders until the new scanner pipeline replaces this.
            context.MediaFiles.Add(new MediaFile
            {
                FilePath = file.FilePath,
                FileSizeBytes = file.SizeBytes,
                Format = file.Format,
                // Fingerprint and LibraryRootId left as defaults (empty / null):
                // the new scanner pipeline populates these via LibraryRoot registration.
                Role = MediaFileRole.Main
            });
            newFiles++;
        }

        // 3. Persist new MediaFile records
        if (newFiles > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Persisted {NewFiles} new MediaFile record(s).", newFiles);
        }

        // 4. Query ALL unlinked MediaFiles (includes pre-existing unlinked ones for retry)
        var unlinked = await context.MediaFiles
            .Where(mf => mf.MediaId == null)
            .ToListAsync(cancellationToken);

        logger.LogInformation(
            "Found {UnlinkedCount} unlinked MediaFile(s) for TMDB auto-match.", unlinked.Count);

        // 5. Delegate TMDB matching to the shared service
        var language = request.Language ?? "en";
        var matchResult = await autoMatchService.MatchAndLinkUnlinkedFilesAsync(
            unlinked, language, cancellationToken);

        logger.LogInformation(
            "Auto-match complete. Matched={Matched}, Skipped={Skipped}, Failed={Failed}.",
            matchResult.Matched, matchResult.Skipped, matchResult.Failed);

        // 6. Aggregate and return
        return Result.Success(new ScanAndImportNasResult(
            NewFiles: newFiles,
            ExistingFiles: existingPaths.Count,
            TotalScanned: files.Count,
            FoldersFound: foldersFound,
            Matched: matchResult.Matched,
            Skipped: matchResult.Skipped,
            Failed: matchResult.Failed,
            Errors: matchResult.Errors));
    }
}

