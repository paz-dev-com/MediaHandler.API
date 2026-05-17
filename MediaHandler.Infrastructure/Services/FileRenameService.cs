// FileRenameService — infrastructure implementation of IFileRenameService.
// Loads MediaFile + associated Media from the database, computes a proposed filename
// per naming conventions, checks for case-insensitive filesystem conflicts, and
// executes an atomic File.Move with compensation on DB save failure.
//
// Naming conventions (from plan.md):
//   Film   : "{Title} ({Year}).{ext}"
//   TV ep  : "{ShowName} - S{ss:D2}E{ee:D2} - {EpisodeName}.{ext}"

using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Dashboard.DTOs;
using MediaHandler.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Infrastructure.Services;

/// <summary>
///     Infrastructure implementation of <see cref="IFileRenameService" />.
///     Computes proposed rename targets and optionally executes an atomic
///     <c>File.Move</c> with database path update and filesystem compensation.
/// </summary>
public sealed class FileRenameService(IApplicationDbContext db) : IFileRenameService
{
    /// <inheritdoc />
    public async Task<Result<FileRenameResultDto>> PreviewRenameAsync(
        Guid mediaFileId,
        CancellationToken ct = default)
    {
        var proposal = await BuildRenameProposalAsync(mediaFileId, ct);
        if (!proposal.IsSuccess)
            return Result.Fail<FileRenameResultDto>(proposal.Errors);

        var (mediaFile, proposedFileName) = proposal.Value;

        var currentFileName = Path.GetFileName(mediaFile.FilePath);
        var dir = Path.GetDirectoryName(mediaFile.FilePath) ?? string.Empty;
        var proposedPath = Path.Combine(dir, proposedFileName);

        // Conflict check — skip when the name is already correct
        if (!string.Equals(currentFileName, proposedFileName, StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(dir))
        {
            var conflict = Directory.GetFiles(dir)
                .Any(f => string.Equals(
                    Path.GetFileName(f), proposedFileName, StringComparison.OrdinalIgnoreCase));

            if (conflict)
                return Result.Fail<FileRenameResultDto>(
                    $"FILE_CONFLICT: A file named '{proposedFileName}' already exists in '{dir}'.");
        }

        return Result.Success(new FileRenameResultDto(
            mediaFileId,
            currentFileName,
            proposedFileName,
            mediaFile.FilePath,
            proposedPath,
            Executed: false));
    }

    /// <inheritdoc />
    public async Task<Result<FileRenameResultDto>> ExecuteRenameAsync(
        Guid mediaFileId,
        CancellationToken ct = default)
    {
        var proposal = await BuildRenameProposalAsync(mediaFileId, ct);
        if (!proposal.IsSuccess)
            return Result.Fail<FileRenameResultDto>(proposal.Errors);

        var (mediaFile, proposedFileName) = proposal.Value;

        var currentFileName = Path.GetFileName(mediaFile.FilePath);
        var currentPath = mediaFile.FilePath;
        var dir = Path.GetDirectoryName(currentPath) ?? string.Empty;
        var proposedPath = Path.Combine(dir, proposedFileName);

        if (!File.Exists(currentPath))
            return Result.Fail<FileRenameResultDto>(
                $"FILE_NOT_FOUND: The file '{currentPath}' does not exist on the filesystem.");

        // Conflict check — skip when the name is already correct
        if (!string.Equals(currentFileName, proposedFileName, StringComparison.OrdinalIgnoreCase))
        {
            var conflict = Directory.GetFiles(dir)
                .Any(f => string.Equals(
                    Path.GetFileName(f), proposedFileName, StringComparison.OrdinalIgnoreCase));

            if (conflict)
                return Result.Fail<FileRenameResultDto>(
                    $"FILE_CONFLICT: A file named '{proposedFileName}' already exists in '{dir}'.");
        }

        // Atomic filesystem rename
        File.Move(currentPath, proposedPath);

        // Persist new path to DB; compensate on failure
        mediaFile.FilePath = proposedPath;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // Best-effort compensation: move file back to original path
            try { File.Move(proposedPath, currentPath); }
            catch { /* ignore secondary failure — original exception propagates */ }

            throw;
        }

        return Result.Success(new FileRenameResultDto(
            mediaFileId,
            currentFileName,
            proposedFileName,
            currentPath,
            proposedPath,
            Executed: true));
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Loads <c>MediaFile</c> with its <c>Media</c> navigation, looks up
    ///     episode metadata for TV shows, and computes the proposed file name.
    /// </summary>
    private async Task<Result<(Domain.Entities.MediaFile MediaFile, string ProposedFileName)>>
        BuildRenameProposalAsync(Guid mediaFileId, CancellationToken ct)
    {
        // Load MediaFile including the linked Media row
        var mediaFile = await db.MediaFiles
            .Include(f => f.Media)
            .FirstOrDefaultAsync(f => f.Id == mediaFileId, ct);

        if (mediaFile is null)
            return Result.Fail<(Domain.Entities.MediaFile, string)>(
                $"MEDIAFILE_NOT_FOUND: MediaFile '{mediaFileId}' was not found.");

        var media = mediaFile.Media;

        if (media is null)
            return Result.Fail<(Domain.Entities.MediaFile, string)>(
                "TMDB_ASSIGNMENT_REQUIRED: This file has no TMDB assignment. " +
                "Reassign TMDB before renaming.");

        var ext = Path.GetExtension(mediaFile.FilePath);
        string proposedFileName;

        if (media.Type == MediaType.Film)
        {
            var year = media.ReleaseDate?.Year ?? media.Year;
            proposedFileName = year.HasValue
                ? $"{Sanitize(media.Title)} ({year.Value}){ext}"
                : $"{Sanitize(media.Title)}{ext}";
        }
        else if (media.Type == MediaType.TvShow)
        {
            // Retrieve ParsedSeason / ParsedEpisode from ScanItemDecision
            var decision = await db.ScanItemDecisions
                .FirstOrDefaultAsync(d => d.MediaFileId == mediaFileId, ct);

            if (decision?.ParsedSeason is null || decision.ParsedEpisode is null)
                return Result.Fail<(Domain.Entities.MediaFile, string)>(
                    "EPISODE_TITLE_NOT_AVAILABLE: Season/episode information is not recorded " +
                    "for this file. Re-scan or manually assign season/episode numbers.");

            // Look up TvEpisode through TvSeason → TvEpisode hierarchy
            var episodeName = await db.TvSeasons
                .Where(s => s.MediaId == media.Id
                            && s.SeasonNumber == decision.ParsedSeason.Value)
                .SelectMany(s => s.TvEpisodes)
                .Where(e => e.EpisodeNumber == decision.ParsedEpisode.Value)
                .Select(e => (string?)e.Name)
                .FirstOrDefaultAsync(ct);

            if (episodeName is null)
                return Result.Fail<(Domain.Entities.MediaFile, string)>(
                    "EPISODE_TITLE_NOT_AVAILABLE: Episode title not available — " +
                    "run TMDB enrichment first.");

            proposedFileName =
                $"{Sanitize(media.Title)} - " +
                $"S{decision.ParsedSeason.Value:D2}E{decision.ParsedEpisode.Value:D2} - " +
                $"{Sanitize(episodeName)}{ext}";
        }
        else
        {
            return Result.Fail<(Domain.Entities.MediaFile, string)>(
                $"UNSUPPORTED_MEDIA_TYPE: Media type '{media.Type}' does not support " +
                "file renaming.");
        }

        return Result.Success<(Domain.Entities.MediaFile, string)>((mediaFile, proposedFileName));
    }

    /// <summary>
    ///     Replaces filesystem-invalid characters with an underscore so that
    ///     TMDB titles (which may contain <c>:</c>, <c>/</c>, etc.) produce valid paths.
    /// </summary>
    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }
}

