using FluentValidation;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Dashboard.DTOs;
using MediaHandler.Application.Features.Dashboard.Queries.ListParentFolders;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MediaEntity = MediaHandler.Domain.Entities.Media;

namespace MediaHandler.Application.Features.Dashboard.Commands.AssignParentFolder;

/// <summary>
///     Assigns a TMDB entry to all media files located inside <see cref="FolderPath" />.
///     Updates <c>ScanItemDecision.AssignedTmdbId/Kind</c> and <c>MediaFile.MediaId</c>
///     for every file in the folder.
/// </summary>
public record AssignParentFolderCommand(
    Guid FolderId,
    string FolderPath,
    int TmdbId,
    MediaType Kind) : IRequest<Result<ParentFolderGroupDto>>;

// =========================================================================
// Validator
// =========================================================================

public class AssignParentFolderCommandValidator : AbstractValidator<AssignParentFolderCommand>
{
    public AssignParentFolderCommandValidator()
    {
        RuleFor(x => x.FolderPath).NotEmpty().WithMessage("FolderPath is required.");
        RuleFor(x => x.TmdbId).GreaterThan(0).WithMessage("TmdbId must be a positive integer.");
        RuleFor(x => x.Kind).IsInEnum().WithMessage("Kind must be Film or TvShow.");
    }
}

// =========================================================================
// Handler
// =========================================================================

public sealed class AssignParentFolderCommandHandler(
    IApplicationDbContext db,
    ITmdbService tmdbService)
    : IRequestHandler<AssignParentFolderCommand, Result<ParentFolderGroupDto>>
{
    public async Task<Result<ParentFolderGroupDto>> Handle(
        AssignParentFolderCommand request,
        CancellationToken cancellationToken)
    {
        // Verify TMDB entry exists
        var lookup = request.Kind == MediaType.Film
            ? await tmdbService.GetMovieByIdAsync(request.TmdbId, cancellationToken: cancellationToken)
            : await tmdbService.GetTvShowByIdAsync(request.TmdbId, cancellationToken: cancellationToken);

        if (lookup is null)
            lookup = request.Kind == MediaType.Film
                ? await tmdbService.GetTvShowByIdAsync(request.TmdbId, cancellationToken: cancellationToken)
                : await tmdbService.GetMovieByIdAsync(request.TmdbId, cancellationToken: cancellationToken);

        if (lookup is null)
            return Result.Fail<ParentFolderGroupDto>(
                $"TMDB_ID_NOT_FOUND: The TMDB id {request.TmdbId} does not correspond to a known movie or TV show.");

        var folder = request.FolderPath.TrimEnd('/', '\\');

        // Load all MediaFiles under this folder
        var mediaFiles = await db.MediaFiles
            .Where(f => f.FilePath.StartsWith(folder + "/") || f.FilePath.StartsWith(folder + "\\"))
            .Include(f => f.Media)
            .ToListAsync(cancellationToken);

        if (mediaFiles.Count == 0)
            return Result.Fail<ParentFolderGroupDto>(
                $"FOLDER_NOT_FOUND: No media files found under '{request.FolderPath}'.");

        // Upsert the Media row
        var media = await db.Medias
            .FirstOrDefaultAsync(m => m.TmdbId == lookup.TmdbId && m.Type == lookup.Kind, cancellationToken);

        if (media is null)
        {
            media = new MediaEntity
            {
                TmdbId = lookup.TmdbId,
                Title = lookup.Title,
                Type = lookup.Kind,
                Year = lookup.Year,
                PosterPath = lookup.PosterPath
            };
            db.Medias.Add(media);
            await db.SaveChangesAsync(cancellationToken); // Flush to get the new Media.Id
        }

        // Load linked ScanItemDecisions for these files
        var fileIds = mediaFiles.Select(f => f.Id).ToList();
        var decisions = await db.ScanItemDecisions
            .Where(d => d.MediaFileId != null && fileIds.Contains(d.MediaFileId!.Value))
            .ToListAsync(cancellationToken);

        // Bulk-update decisions
        foreach (var decision in decisions)
        {
            decision.AssignedTmdbId = lookup.TmdbId;
            decision.AssignedTmdbKind = lookup.Kind;
        }

        // Link MediaFiles to the Media row
        foreach (var mf in mediaFiles)
            mf.MediaId = media.Id;

        await db.SaveChangesAsync(cancellationToken);

        var dto = new ParentFolderGroupDto(
            request.FolderId,
            request.FolderPath,
            GetLastSegment(folder),
            mediaFiles.Count,
            "Assigned",
            lookup.TmdbId,
            lookup.Title);

        return Result.Success(dto);
    }

    private static string GetLastSegment(string folderPath)
    {
        var normalized = folderPath.TrimEnd('/').Replace('\\', '/');
        var lastSlash = normalized.LastIndexOf('/');
        return lastSlash >= 0 ? normalized[(lastSlash + 1)..] : normalized;
    }
}

