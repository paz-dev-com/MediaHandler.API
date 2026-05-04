// RenameFileCommand — command, validator, and handler for renaming a single media file
// to match TMDB naming conventions.
// The handler validates that a TMDB assignment exists and that episode metadata is
// available (TV shows only), then delegates the actual rename (preview or execute)
// to IFileRenameService.

using FluentValidation;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Dashboard.DTOs;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Dashboard.Commands.RenameFile;

/// <summary>Command to preview or execute a TMDB-convention rename on a single media file.</summary>
public record RenameFileCommand(
    Guid MediaFileId,
    bool Preview) : IRequest<Result<FileRenameResultDto>>;

// =========================================================================
// Validator
// =========================================================================

public class RenameFileCommandValidator : AbstractValidator<RenameFileCommand>
{
    public RenameFileCommandValidator()
    {
        RuleFor(x => x.MediaFileId)
            .NotEmpty().WithMessage("MediaFileId is required.");
    }
}

// =========================================================================
// Handler
// =========================================================================

/// <summary>
///     Handles <see cref="RenameFileCommand" />.
///     <list type="bullet">
///         <item>Loads <c>MediaFile</c> and associated <c>Media</c>; returns failure if not found.</item>
///         <item>Returns <c>TMDB_ASSIGNMENT_REQUIRED</c> when no media entry is linked.</item>
///         <item>For TV episodes: loads the <c>ScanItemDecision</c> to retrieve <c>ParsedSeason</c>
///              and <c>ParsedEpisode</c>, then verifies a <c>TvEpisode</c> record exists.
///              Returns <c>EPISODE_TITLE_NOT_AVAILABLE</c> if no record is found.</item>
///         <item>Delegates preview or execution to <see cref="IFileRenameService" />.</item>
///     </list>
/// </summary>
public sealed class RenameFileCommandHandler(
    IApplicationDbContext db,
    IFileRenameService fileRenameService)
    : IRequestHandler<RenameFileCommand, Result<FileRenameResultDto>>
{
    public async Task<Result<FileRenameResultDto>> Handle(
        RenameFileCommand request,
        CancellationToken cancellationToken)
    {
        // Load MediaFile with Media navigation
        var mediaFile = await db.MediaFiles
            .Include(f => f.Media)
            .FirstOrDefaultAsync(f => f.Id == request.MediaFileId, cancellationToken);

        if (mediaFile is null)
            return Result.Fail<FileRenameResultDto>(
                $"MEDIAFILE_NOT_FOUND: MediaFile '{request.MediaFileId}' was not found.");

        var media = mediaFile.Media;

        if (media is null)
            return Result.Fail<FileRenameResultDto>(
                "TMDB_ASSIGNMENT_REQUIRED: This file has no TMDB assignment. " +
                "Reassign TMDB before renaming.");

        // TV-specific validation: episode title must be available before rename
        if (media.Type == MediaType.TvShow)
        {
            var decision = await db.ScanItemDecisions
                .FirstOrDefaultAsync(d => d.MediaFileId == request.MediaFileId, cancellationToken);

            if (decision?.ParsedSeason is null || decision.ParsedEpisode is null)
                return Result.Fail<FileRenameResultDto>(
                    "EPISODE_TITLE_NOT_AVAILABLE: Season/episode information is not recorded " +
                    "for this file. Re-scan or manually assign season/episode numbers.");

            var episodeExists = await db.TvSeasons
                .Where(s => s.MediaId == media.Id
                            && s.SeasonNumber == decision.ParsedSeason.Value)
                .SelectMany(s => s.TvEpisodes)
                .AnyAsync(e => e.EpisodeNumber == decision.ParsedEpisode.Value, cancellationToken);

            if (!episodeExists)
                return Result.Fail<FileRenameResultDto>(
                    "EPISODE_TITLE_NOT_AVAILABLE: Episode title not available — " +
                    "run TMDB enrichment first.");
        }

        // Delegate the rename (or preview) to the infrastructure service
        return request.Preview
            ? await fileRenameService.PreviewRenameAsync(request.MediaFileId, cancellationToken)
            : await fileRenameService.ExecuteRenameAsync(request.MediaFileId, cancellationToken);
    }
}

