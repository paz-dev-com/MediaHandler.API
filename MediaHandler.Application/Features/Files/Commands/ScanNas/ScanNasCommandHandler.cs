using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Files.Commands.ScanNas;

public record ScanNasCommand(string? BasePath = null) : IRequest<Result<ScanNasResult>>;
public record ScanNasResult(int NewFiles, int ExistingFiles, int TotalScanned, int FoldersFound);

public class ScanNasCommandHandler(IApplicationDbContext context, INasService nas)
    : IRequestHandler<ScanNasCommand, Result<ScanNasResult>>
{
    public async Task<Result<ScanNasResult>> Handle(ScanNasCommand request, CancellationToken cancellationToken)
    {
        var entries = (await nas.ScanDirectoryAsync(request.BasePath, cancellationToken)).ToList();

        var files = entries.Where(e => !e.IsDirectory).ToList();
        var foldersFound = entries.Count(e => e.IsDirectory);

        var existingPaths = await context.MediaFiles
            .Select(mf => mf.FilePath)
            .ToHashSetAsync(cancellationToken);

        var newFiles = 0;
        foreach (var file in files)
        {
            if (existingPaths.Contains(file.FilePath))
                continue;

            // Legacy handler — placeholders per data-model §3.2 until T115 retires this code.
            context.MediaFiles.Add(new MediaFile
            {
                FilePath = file.FilePath,
                FileSizeBytes = file.SizeBytes,
                Format = file.Format,
                Fingerprint = $"{file.FilePath}|{file.SizeBytes}|0",
                LibraryRootId = Guid.Empty,   // sentinel "Legacy" root; corrected by migration
                Role = MediaFileRole.Main
            });
            newFiles++;
        }

        if (newFiles > 0)
            await context.SaveChangesAsync(cancellationToken);

        return Result.Success(new ScanNasResult(
            NewFiles: newFiles,
            ExistingFiles: existingPaths.Count,
            TotalScanned: files.Count,
            FoldersFound: foldersFound));
    }
}
