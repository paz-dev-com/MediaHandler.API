using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Files.Commands.ScanNas;

public record ScanNasCommand(string? BasePath = null) : IRequest<Result<ScanNasResult>>;
public record ScanNasResult(int NewFiles, int ExistingFiles, int TotalScanned);

public class ScanNasCommandHandler : IRequestHandler<ScanNasCommand, Result<ScanNasResult>>
{
    private readonly IApplicationDbContext _context;
    private readonly INasService _nas;

    public ScanNasCommandHandler(IApplicationDbContext context, INasService nas)
    {
        _context = context;
        _nas = nas;
    }

    public async Task<Result<ScanNasResult>> Handle(ScanNasCommand request, CancellationToken cancellationToken)
    {
        var basePath = request.BasePath ?? string.Empty;
        var files = await _nas.ScanDirectoryAsync(basePath, cancellationToken);

        var existingPaths = await _context.MediaFiles
            .Select(mf => mf.FilePath)
            .ToHashSetAsync(cancellationToken);

        var newFiles = 0;

        foreach (var file in files)
        {
            if (existingPaths.Contains(file.FilePath))
                continue;

            _context.MediaFiles.Add(new MediaFile
            {
                MediaId = Guid.Empty,
                FilePath = file.FilePath,
                FileSizeBytes = file.SizeBytes,
                Format = file.Format
            });
            newFiles++;
        }

        if (newFiles > 0)
            await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new ScanNasResult(newFiles, existingPaths.Count, newFiles + existingPaths.Count));
    }
}
