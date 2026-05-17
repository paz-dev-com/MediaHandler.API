using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Dashboard.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Dashboard.Queries.ListParentFolders;

/// <summary>
///     Returns a paginated list of unique NAS parent folders aggregated from
///     <c>MediaFile.FilePath</c>, with TMDB assignment status per folder.
/// </summary>
public record ListParentFoldersQuery(
    string? Status,
    int Page,
    int PageSize) : IRequest<PagedResult<ParentFolderGroupDto>>;

// =========================================================================
// Validator
// =========================================================================

public class ListParentFoldersQueryValidator : AbstractValidator<ListParentFoldersQuery>
{
    private static readonly HashSet<string> AllowedStatuses =
        new(["NotAssigned", "Assigned", "InCollection"], StringComparer.OrdinalIgnoreCase);

    public ListParentFoldersQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1).WithMessage("page must be ≥ 1.");
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100).WithMessage("pageSize must be between 1 and 100.");

        When(x => x.Status is not null, () =>
        {
            RuleFor(x => x.Status)
                .Must(s => AllowedStatuses.Contains(s!))
                .WithMessage("status must be NotAssigned, Assigned, or InCollection.");
        });
    }
}

// =========================================================================
// Handler
// =========================================================================

public sealed class ListParentFoldersQueryHandler(IApplicationDbContext db)
    : IRequestHandler<ListParentFoldersQuery, PagedResult<ParentFolderGroupDto>>
{
    public async Task<PagedResult<ParentFolderGroupDto>> Handle(
        ListParentFoldersQuery request,
        CancellationToken cancellationToken)
    {
        // Load all MediaFiles with related ScanItemDecision and Media to compute folder groups in memory.
        // This is acceptable because the MediaFile table is bounded by the NAS library size.
        var files = await db.MediaFiles
            .AsNoTracking()
            .Include(f => f.Media)
            .ToListAsync(cancellationToken);

        // Load ScanItemDecisions that reference those media files (latest per MediaFileId)
        var mediaFileIds = files.Select(f => f.Id).ToHashSet();
        var decisions = await db.ScanItemDecisions
            .AsNoTracking()
            .Where(d => d.MediaFileId != null && mediaFileIds.Contains(d.MediaFileId!.Value))
            .ToListAsync(cancellationToken);

        var decisionByFile = decisions
            .GroupBy(d => d.MediaFileId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(d => d.CreatedAt).First());

        // Group files by parent directory
        var groups = files
            .GroupBy(f => GetParentFolder(f.FilePath))
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .Select(g =>
            {
                var folderPath = g.Key;
                var detectedShowName = GetLastSegment(folderPath);
                var episodeCount = g.Count();

                // Determine status from the most-enriched file in the group
                string status = "NotAssigned";
                int? tmdbId = null;
                string? tmdbTitle = null;

                // InCollection: any file links to a Media row with overview populated
                var inCollection = g.FirstOrDefault(f =>
                    f.MediaId.HasValue && f.Media?.Overview != null);
                if (inCollection?.Media != null)
                {
                    status = "InCollection";
                    tmdbId = inCollection.Media.TmdbId;
                    tmdbTitle = inCollection.Media.Title;
                }
                else
                {
                    // Assigned: any linked ScanItemDecision has AssignedTmdbId
                    var assigned = g
                        .Where(f => decisionByFile.ContainsKey(f.Id))
                        .Select(f => decisionByFile[f.Id])
                        .FirstOrDefault(d => d.AssignedTmdbId.HasValue);

                    if (assigned != null)
                    {
                        status = "Assigned";
                        tmdbId = assigned.AssignedTmdbId;
                    }
                }

                var folderId = ComputeFolderId(folderPath);

                return new ParentFolderGroupDto(folderId, folderPath, detectedShowName, episodeCount, status,
                    tmdbId, tmdbTitle);
            })
            .AsQueryable();

        // Filter by status
        if (!string.IsNullOrEmpty(request.Status))
            groups = groups.Where(g =>
                string.Equals(g.Status, request.Status, StringComparison.OrdinalIgnoreCase));

        var ordered = groups.OrderBy(g => g.FolderPath).ToList();
        var totalCount = ordered.Count;
        var items = ordered
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        return new PagedResult<ParentFolderGroupDto>(
            items,
            request.Page,
            request.PageSize,
            totalCount);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static string GetParentFolder(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return string.Empty;

        var normalized = filePath.Replace('\\', '/');
        var lastSlash = normalized.LastIndexOf('/');
        return lastSlash > 0 ? normalized[..lastSlash] : string.Empty;
    }

    private static string GetLastSegment(string folderPath)
    {
        var normalized = folderPath.TrimEnd('/').Replace('\\', '/');
        var lastSlash = normalized.LastIndexOf('/');
        return lastSlash >= 0 ? normalized[(lastSlash + 1)..] : normalized;
    }

    /// <summary>Deterministic GUID from SHA-256 of the lower-invariant folder path.</summary>
    public static Guid ComputeFolderId(string folderPath)
    {
        var input = folderPath.ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50); // version 5
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // variant RFC 4122
        return new Guid(hash[..16]);
    }
}

