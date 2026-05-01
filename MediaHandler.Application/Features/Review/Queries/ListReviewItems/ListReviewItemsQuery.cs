// ListReviewItems — paginated query for the admin review queue.
// Supports filtering by status, reason, and scanRunId.

using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using MediaHandler.Application.Common.DTOs;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Review.Queries.ListReviewItems;

/// <summary>
///     Query parameters for listing review items.
///     Defaults to <see cref="ReviewStatus.Open" /> items only.
/// </summary>
public record ListReviewItemsQuery(
    ReviewStatus? Status = ReviewStatus.Open,
    ReviewReason? Reason = null,
    Guid? ScanRunId = null,
    int Page = 1,
    int PageSize = 25) : IRequest<Result<PagedResult<ReviewItemDto>>>;

// =========================================================================
// Validator
// =========================================================================

public class ListReviewItemsQueryValidator : AbstractValidator<ListReviewItemsQuery>
{
    public ListReviewItemsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0).WithMessage("Page must be at least 1.");
        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100)
            .WithMessage("PageSize must be between 1 and 100.");
    }
}

// =========================================================================
// Handler
// =========================================================================

/// <summary>
///     Returns a paginated list of <see cref="ReviewItemDto" />s, optionally filtered by
///     <see cref="ReviewStatus" />, <see cref="ReviewReason" />, and scan run id.
/// </summary>
public sealed class ListReviewItemsQueryHandler(IApplicationDbContext db)
    : IRequestHandler<ListReviewItemsQuery, Result<PagedResult<ReviewItemDto>>>
{
    public async Task<Result<PagedResult<ReviewItemDto>>> Handle(
        ListReviewItemsQuery request,
        CancellationToken cancellationToken)
    {
        var query = db.ReviewItems.AsNoTracking();

        // Apply filters
        if (request.Status.HasValue)
            query = query.Where(r => r.Status == request.Status.Value);

        if (request.Reason.HasValue)
            query = query.Where(r => r.Reason == request.Reason.Value);

        if (request.ScanRunId.HasValue)
            query = query.Where(r => r.FirstSeenScanRunId == request.ScanRunId.Value);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var dtos = items.Select(MapToDto).ToList();

        return Result.Success(new PagedResult<ReviewItemDto>(
            dtos,
            totalCount,
            request.Page,
            request.PageSize));
    }

    private static ReviewItemDto MapToDto(ReviewItem item)
    {
        // Deserialize candidates JSON
        IReadOnlyList<TmdbCandidateDto> candidates = [];
        try
        {
            if (!string.IsNullOrWhiteSpace(item.CandidatesJson) && item.CandidatesJson != "[]")
            {
                var raw = JsonSerializer.Deserialize<List<CandidateJson>>(item.CandidatesJson);
                candidates = raw?.Select(c => new TmdbCandidateDto(
                        c.TmdbId, Enum.Parse<MediaType>(c.Kind, true), c.Title, c.Year, c.Score, c.PosterPath))
                    .ToList() ?? [];
            }
        }
        catch
        {
            /* ignore malformed JSON */
        }

        return new ReviewItemDto(
            item.Id,
            item.FilePath,
            item.Reason,
            item.Status,
            item.ParsedTitle,
            item.ParsedYear,
            item.ParsedSeason,
            item.ParsedEpisode,
            candidates,
            item.ResolvedTmdbId,
            item.ResolvedKind,
            item.ResolvedAt,
            item.CreatedAt);
    }

    private record CandidateJson(
        [property: JsonPropertyName("tmdbId")] int TmdbId,
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("year")] int? Year,
        [property: JsonPropertyName("score")] decimal Score,
        [property: JsonPropertyName("posterPath")]
        string? PosterPath);
}