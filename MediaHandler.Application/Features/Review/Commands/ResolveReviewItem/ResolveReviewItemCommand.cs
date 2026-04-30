#nullable enable
// ResolveReviewItem — command, handler, and validator for admin review-item resolution.
// Supports three actions: Assign (map to TMDB id), Dismiss (acknowledge without mapping), Delete (remove file).

using FluentValidation;
using MediaHandler.Application.Common.DTOs;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Review.Commands.ResolveReviewItem;

/// <summary>Command to resolve an open <c>ReviewItem</c>.</summary>
public record ResolveReviewItemCommand(
    Guid ReviewItemId,
    ReviewResolutionAction Action,
    int? TmdbId,
    MediaType? Kind) : IRequest<Result<ReviewItemDto>>;

// =========================================================================
// Validator
// =========================================================================

public class ResolveReviewItemCommandValidator : AbstractValidator<ResolveReviewItemCommand>
{
    public ResolveReviewItemCommandValidator()
    {
        RuleFor(x => x.ReviewItemId)
            .NotEmpty().WithMessage("ReviewItemId is required.");

        RuleFor(x => x.Action)
            .IsInEnum().WithMessage("Action must be Assign, Dismiss, or Delete.");

        // TmdbId is required when Action = Assign
        RuleFor(x => x.TmdbId)
            .NotNull().WithMessage("TmdbId is required when Action is Assign.")
            .When(x => x.Action == ReviewResolutionAction.Assign);

        RuleFor(x => x.TmdbId)
            .GreaterThan(0).WithMessage("TmdbId must be a positive integer.")
            .When(x => x.Action == ReviewResolutionAction.Assign && x.TmdbId.HasValue);

        // Kind is required when Action = Assign
        RuleFor(x => x.Kind)
            .NotNull().WithMessage("Kind is required when Action is Assign.")
            .When(x => x.Action == ReviewResolutionAction.Assign);

        RuleFor(x => x.Kind)
            .IsInEnum().WithMessage("Kind must be Film or TvShow.")
            .When(x => x.Action == ReviewResolutionAction.Assign && x.Kind.HasValue);
    }
}

// =========================================================================
// Handler
// =========================================================================

/// <summary>
/// Handles <see cref="ResolveReviewItemCommand"/>.
/// <list type="bullet">
///   <item><b>Assign</b>: verifies the TMDB id is real, writes resolution fields, status → Resolved.</item>
///   <item><b>Dismiss</b>: marks status → Dismissed.</item>
///   <item><b>Delete</b>: removes the underlying <see cref="MediaFile"/> (and orphaned parents), marks Dismissed.</item>
/// </list>
/// </summary>
public sealed class ResolveReviewItemCommandHandler(
    IApplicationDbContext db,
    ITmdbService tmdbService,
    ICurrentUserService currentUser)
    : IRequestHandler<ResolveReviewItemCommand, Result<ReviewItemDto>>
{
    public async Task<Result<ReviewItemDto>> Handle(
        ResolveReviewItemCommand request,
        CancellationToken cancellationToken)
    {
        // Load the review item
        var reviewItem = await db.ReviewItems
            .FirstOrDefaultAsync(r => r.Id == request.ReviewItemId, cancellationToken);

        if (reviewItem is null)
            return Result.Fail<ReviewItemDto>($"ReviewItem '{request.ReviewItemId}' was not found.");

        // Must be Open to be resolved
        if (reviewItem.Status != ReviewStatus.Open)
            return Result.Fail<ReviewItemDto>(
                $"REVIEW_ALREADY_RESOLVED: ReviewItem '{request.ReviewItemId}' is already {reviewItem.Status}.");

        switch (request.Action)
        {
            case ReviewResolutionAction.Assign:
                return await HandleAssignAsync(reviewItem, request, cancellationToken);

            case ReviewResolutionAction.Dismiss:
                return await HandleDismissAsync(reviewItem, cancellationToken);

            case ReviewResolutionAction.Delete:
                return await HandleDeleteAsync(reviewItem, cancellationToken);

            default:
                return Result.Fail<ReviewItemDto>($"Unknown action: {request.Action}");
        }
    }

    // =========================================================================
    // Assign action
    // =========================================================================

    private async Task<Result<ReviewItemDto>> HandleAssignAsync(
        Domain.Entities.ReviewItem reviewItem,
        ResolveReviewItemCommand request,
        CancellationToken ct)
    {
        var tmdbId = request.TmdbId!.Value;
        var kind = request.Kind!.Value;

        // Verify the TMDB id actually exists
        TmdbIdLookupResult? lookup = kind == MediaType.Film
            ? await tmdbService.GetMovieByIdAsync(tmdbId, cancellationToken: ct)
            : await tmdbService.GetTvShowByIdAsync(tmdbId, cancellationToken: ct);

        // Try the other kind if not found with the specified one
        if (lookup is null)
        {
            lookup = kind == MediaType.Film
                ? await tmdbService.GetTvShowByIdAsync(tmdbId, cancellationToken: ct)
                : await tmdbService.GetMovieByIdAsync(tmdbId, cancellationToken: ct);
        }

        if (lookup is null)
            return Result.Fail<ReviewItemDto>(
                $"TMDB_ID_NOT_FOUND: The TMDB id {tmdbId} does not correspond to a known movie or TV show.");

        // Persist the resolution
        reviewItem.Status = ReviewStatus.Resolved;
        reviewItem.ResolvedTmdbId = tmdbId;
        reviewItem.ResolvedKind = kind;
        reviewItem.ResolvedBy = currentUser.OktaId;
        reviewItem.ResolvedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        return Result.Success(MapToDto(reviewItem));
    }

    // =========================================================================
    // Dismiss action
    // =========================================================================

    private async Task<Result<ReviewItemDto>> HandleDismissAsync(
        Domain.Entities.ReviewItem reviewItem,
        CancellationToken ct)
    {
        reviewItem.Status = ReviewStatus.Dismissed;
        reviewItem.ResolvedBy = currentUser.OktaId;
        reviewItem.ResolvedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        return Result.Success(MapToDto(reviewItem));
    }

    // =========================================================================
    // Delete action
    // =========================================================================

    private async Task<Result<ReviewItemDto>> HandleDeleteAsync(
        Domain.Entities.ReviewItem reviewItem,
        CancellationToken ct)
    {
        // Remove underlying MediaFile(s) matching the review item's file path
        var mediaFilesToRemove = await db.MediaFiles
            .Where(mf => mf.FilePath == reviewItem.FilePath)
            .ToListAsync(ct);

        if (mediaFilesToRemove.Count > 0)
        {
            db.MediaFiles.RemoveRange(mediaFilesToRemove);
        }

        reviewItem.Status = ReviewStatus.Dismissed;
        reviewItem.ResolvedBy = currentUser.OktaId;
        reviewItem.ResolvedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        return Result.Success(MapToDto(reviewItem));
    }

    // =========================================================================
    // DTO mapping
    // =========================================================================

    private static ReviewItemDto MapToDto(Domain.Entities.ReviewItem item)
    {
        // Deserialize candidates JSON
        IReadOnlyList<TmdbCandidateDto> candidates = [];
        try
        {
            if (!string.IsNullOrWhiteSpace(item.CandidatesJson) && item.CandidatesJson != "[]")
            {
                var raw = System.Text.Json.JsonSerializer.Deserialize<List<CandidateJson>>(item.CandidatesJson);
                candidates = raw?.Select(c => new TmdbCandidateDto(
                    c.TmdbId, Enum.Parse<MediaType>(c.Kind, true), c.Title, c.Year, c.Score, c.PosterPath))
                    .ToList() ?? [];
            }
        }
        catch { /* ignore malformed JSON — return empty list */ }

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
        [property: System.Text.Json.Serialization.JsonPropertyName("tmdbId")] int TmdbId,
        [property: System.Text.Json.Serialization.JsonPropertyName("kind")] string Kind,
        [property: System.Text.Json.Serialization.JsonPropertyName("title")] string Title,
        [property: System.Text.Json.Serialization.JsonPropertyName("year")] int? Year,
        [property: System.Text.Json.Serialization.JsonPropertyName("score")] decimal Score,
        [property: System.Text.Json.Serialization.JsonPropertyName("posterPath")] string? PosterPath);
}

