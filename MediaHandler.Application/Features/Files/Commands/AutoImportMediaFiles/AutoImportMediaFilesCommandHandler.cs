using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaHandler.Application.Features.Files.Commands.AutoImportMediaFiles;

/// <summary>
///     Queries all <c>MediaFile</c> records where <c>MediaId IS NULL</c> and delegates them
///     to <see cref="IMediaAutoMatchService" /> for TMDB matching and linking.
///     Does not trigger a NAS scan — useful for retrying previously failed or skipped files.
/// </summary>
public class AutoImportMediaFilesCommandHandler(
    IApplicationDbContext context,
    IMediaAutoMatchService autoMatchService,
    ILogger<AutoImportMediaFilesCommandHandler> logger)
    : IRequestHandler<AutoImportMediaFilesCommand, Result<AutoImportResult>>
{
    public async Task<Result<AutoImportResult>> Handle(
        AutoImportMediaFilesCommand request,
        CancellationToken cancellationToken)
    {
        // Query only unlinked MediaFiles
        var unlinked = await context.MediaFiles
            .Where(mf => mf.MediaId == null)
            .ToListAsync(cancellationToken);

        logger.LogInformation(
            "Auto-import triggered. Found {UnlinkedCount} unlinked MediaFile(s).",
            unlinked.Count);

        if (unlinked.Count == 0)
        {
            logger.LogInformation("No unlinked MediaFiles found. Nothing to import.");
            return Result.Success(new AutoImportResult(
                0,
                0,
                0,
                0,
                []));
        }

        var language = request.Language ?? "en";

        var matchResult = await autoMatchService.MatchAndLinkUnlinkedFilesAsync(
            unlinked, language, cancellationToken);

        logger.LogInformation(
            "Auto-import complete. TotalUnlinked={Total}, Matched={Matched}, Skipped={Skipped}, Failed={Failed}.",
            unlinked.Count, matchResult.Matched, matchResult.Skipped, matchResult.Failed);

        return Result.Success(new AutoImportResult(
            unlinked.Count,
            matchResult.Matched,
            matchResult.Skipped,
            matchResult.Failed,
            matchResult.Errors));
    }
}