// BatchRenameTvGroupCommand — command, validator, and handler for batch-renaming all
// episode files in a TV show group to TMDB naming conventions.
//
// Safety contract (all-or-nothing):
//   1. Resolve group members by recomputing the deterministic GroupId for the scan.
//   2. Validate TMDB assignment is present on the group.
//   3. Load TvEpisode records for EVERY episode in the group.
//      → Return 422 if ANY are missing (refuse to execute partial rename).
//   4. Call PreviewRenameAsync for EVERY file to check for filesystem conflicts.
//      → Return 422 if ANY proposed target conflicts with an existing file.
//   5a. Preview mode  → return the collected preview results without touching the FS.
//   5b. Execute mode  → call ExecuteRenameAsync for each file in order.

using FluentValidation;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Dashboard.DTOs;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Dashboard.Commands.BatchRenameTvGroup;

/// <summary>Application-layer result returned by <see cref="BatchRenameTvGroupCommandHandler" />.</summary>
public record BatchRenameTvGroupResult(
    Guid GroupId,
    string ParsedShowName,
    IReadOnlyList<FileRenameResultDto> Episodes);

/// <summary>Command to preview or execute batch TMDB-convention rename for a TV show group.</summary>
public record BatchRenameTvGroupCommand(
    Guid GroupId,
    Guid ScanId,
    bool Preview) : IRequest<Result<BatchRenameTvGroupResult>>;

// =========================================================================
// Validator
// =========================================================================

public class BatchRenameTvGroupCommandValidator : AbstractValidator<BatchRenameTvGroupCommand>
{
    public BatchRenameTvGroupCommandValidator()
    {
        RuleFor(x => x.GroupId)
            .NotEmpty().WithMessage("GroupId is required.");

        RuleFor(x => x.ScanId)
            .NotEmpty().WithMessage("ScanId is required.");
    }
}

// =========================================================================
// Handler
// =========================================================================

/// <summary>
///     Handles <see cref="BatchRenameTvGroupCommand" />.
///     <list type="bullet">
///         <item>Resolves group members by recomputing the deterministic
///              <c>GroupId = SHA256(scanId|parsedTitle.ToLower())</c> — same logic as
///              <c>AssignTvGroupCommandHandler</c>.</item>
///         <item>Validates TMDB assignment is present on all group members.</item>
///         <item>Validates <c>TvEpisode</c> records exist for every episode.
///              Returns 422 if ANY are missing — refuses partial rename.</item>
///         <item>Previews ALL proposed renames first to catch filesystem conflicts.
///              Returns 422 if ANY conflict is detected — refuses partial rename.</item>
///         <item>In preview mode: returns all proposed renames without executing.</item>
///         <item>In execute mode: calls <see cref="IFileRenameService.ExecuteRenameAsync" />
///              for each file in order.</item>
///     </list>
/// </summary>
public sealed class BatchRenameTvGroupCommandHandler(
    IApplicationDbContext db,
    IFileRenameService fileRenameService)
    : IRequestHandler<BatchRenameTvGroupCommand, Result<BatchRenameTvGroupResult>>
{
    public async Task<Result<BatchRenameTvGroupResult>> Handle(
        BatchRenameTvGroupCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Resolve group members
        var allTvDecisions = await db.ScanItemDecisions
            .Include(d => d.MediaFile)
                .ThenInclude(f => f!.Media)
            .Where(d => d.ScanRunId == request.ScanId
                        && d.ParsedMediaType == MediaType.TvShow
                        && d.ParsedTitle != null)
            .ToListAsync(cancellationToken);

        var matchingDecisions = allTvDecisions
            .Where(d => TvShowGroup.ComputeGroupId(request.ScanId, d.ParsedTitle!) == request.GroupId)
            .ToList();

        if (matchingDecisions.Count == 0)
            return Result.Fail<BatchRenameTvGroupResult>(
                $"GROUP_NOT_FOUND: No TV show group with id '{request.GroupId}' " +
                $"found in scan '{request.ScanId}'.");

        var parsedShowName = matchingDecisions[0].ParsedTitle!;

        // 2. Validate TMDB assignment
        var unassigned = matchingDecisions
            .Where(d => d.MediaFileId is null || d.MediaFile is null || d.MediaFile.MediaId is null)
            .ToList();

        if (unassigned.Count > 0)
            return Result.Fail<BatchRenameTvGroupResult>(
                $"TMDB_ASSIGNMENT_REQUIRED: {unassigned.Count} episode(s) in this group have " +
                "no TMDB assignment. Run group assignment first.");

        // 3. Validate season/episode numbers and TvEpisode records
        var withoutEpisodeInfo = matchingDecisions
            .Where(d => d.ParsedSeason is null || d.ParsedEpisode is null)
            .ToList();

        if (withoutEpisodeInfo.Count > 0)
            return Result.Fail<BatchRenameTvGroupResult>(
                $"EPISODE_TITLE_NOT_AVAILABLE: {withoutEpisodeInfo.Count} episode(s) are " +
                "missing season/episode number information. Re-scan to populate this data.");

        // All media files in the group share the same Media (TV show)
        var mediaId = matchingDecisions[0].MediaFile!.MediaId!.Value;

        // Load available (season, episode) tuples in one DB round-trip
        var availableEpisodes = await db.TvSeasons
            .Where(s => s.MediaId == mediaId)
            .SelectMany(s => s.TvEpisodes.Select(e => new { s.SeasonNumber, e.EpisodeNumber }))
            .ToListAsync(cancellationToken);

        var availableSet = availableEpisodes
            .Select(e => (e.SeasonNumber, e.EpisodeNumber))
            .ToHashSet();

        var missingEpisodes = matchingDecisions
            .Where(d => !availableSet.Contains((d.ParsedSeason!.Value, d.ParsedEpisode!.Value)))
            .ToList();

        if (missingEpisodes.Count > 0)
            return Result.Fail<BatchRenameTvGroupResult>(
                $"EPISODE_TITLE_NOT_AVAILABLE: Episode title not available for " +
                $"{missingEpisodes.Count} episode(s) — run TMDB enrichment first.");

        // 4. Preview ALL renames to validate filesystem conflicts
        var previewResults = new List<FileRenameResultDto>(matchingDecisions.Count);

        foreach (var decision in matchingDecisions)
        {
            var preview = await fileRenameService.PreviewRenameAsync(
                decision.MediaFileId!.Value, cancellationToken);

            if (!preview.IsSuccess)
                return Result.Fail<BatchRenameTvGroupResult>(preview.Errors);

            previewResults.Add(preview.Value);
        }

        // 5a. Preview mode — return without executing
        if (request.Preview)
            return Result.Success(
                new BatchRenameTvGroupResult(request.GroupId, parsedShowName, previewResults));

        // 5b. Execute all renames
        var executeResults = new List<FileRenameResultDto>(matchingDecisions.Count);

        foreach (var decision in matchingDecisions)
        {
            var execute = await fileRenameService.ExecuteRenameAsync(
                decision.MediaFileId!.Value, cancellationToken);

            if (!execute.IsSuccess)
                return Result.Fail<BatchRenameTvGroupResult>(execute.Errors);

            executeResults.Add(execute.Value);
        }

        return Result.Success(
            new BatchRenameTvGroupResult(request.GroupId, parsedShowName, executeResults));
    }
}

