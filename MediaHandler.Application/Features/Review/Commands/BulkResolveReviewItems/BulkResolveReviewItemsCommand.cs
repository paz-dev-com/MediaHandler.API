using FluentValidation;
using MediaHandler.Application.Common.DTOs;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Review.Commands.BulkResolveReviewItems;

/// <summary>
///     Resolves all Open <c>ReviewItem</c> rows whose <c>FilePath</c> starts with
///     <see cref="ParentFolderPath" />, applying the same <see cref="Action" /> to every one.
/// </summary>
public record BulkResolveReviewItemsCommand(
    string ParentFolderPath,
    ReviewResolutionAction Action,
    int? TmdbId,
    MediaType? Kind) : IRequest<Result<BulkResolveResult>>;

// =========================================================================
// Validator
// =========================================================================

public class BulkResolveReviewItemsCommandValidator : AbstractValidator<BulkResolveReviewItemsCommand>
{
    public BulkResolveReviewItemsCommandValidator()
    {
        RuleFor(x => x.ParentFolderPath)
            .NotEmpty().WithMessage("ParentFolderPath is required.");

        RuleFor(x => x.Action)
            .IsInEnum().WithMessage("Action must be Assign, Dismiss, or Delete.");

        RuleFor(x => x.TmdbId)
            .NotNull().WithMessage("TmdbId is required when Action is Assign.")
            .When(x => x.Action == ReviewResolutionAction.Assign);

        RuleFor(x => x.TmdbId)
            .GreaterThan(0).WithMessage("TmdbId must be a positive integer.")
            .When(x => x.Action == ReviewResolutionAction.Assign && x.TmdbId.HasValue);

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

public sealed class BulkResolveReviewItemsCommandHandler(
    IApplicationDbContext db,
    ITmdbService tmdbService,
    ICurrentUserService currentUser)
    : IRequestHandler<BulkResolveReviewItemsCommand, Result<BulkResolveResult>>
{
    public async Task<Result<BulkResolveResult>> Handle(
        BulkResolveReviewItemsCommand request,
        CancellationToken cancellationToken)
    {
        // Normalize folder path — strip trailing separator so prefix match is safe
        var folder = request.ParentFolderPath.TrimEnd('/', '\\');

        // Pre-compute prefixes outside the LINQ expression tree so EF Core
        var prefixUnix = folder + "/";
        var prefixWin = folder + "\\";

        // Load all Open ReviewItems under the folder (prefix match via EF Core)
        var items = await db.ReviewItems
            .Where(r => r.Status == ReviewStatus.Open
                        && (r.FilePath.StartsWith(prefixUnix) || r.FilePath.StartsWith(prefixWin)))
            .ToListAsync(cancellationToken);

        if (items.Count == 0)
            return Result.Fail<BulkResolveResult>(
                $"NO_ITEMS_FOUND: No open review items were found under '{request.ParentFolderPath}'.");

        // For Assign — verify the TMDB id once before touching any rows
        if (request.Action == ReviewResolutionAction.Assign)
        {
            var tmdbId = request.TmdbId!.Value;
            var kind = request.Kind!.Value;

            var lookup = kind == MediaType.Film
                ? await tmdbService.GetMovieByIdAsync(tmdbId, cancellationToken: cancellationToken)
                : await tmdbService.GetTvShowByIdAsync(tmdbId, cancellationToken: cancellationToken);

            if (lookup is null)
                lookup = kind == MediaType.Film
                    ? await tmdbService.GetTvShowByIdAsync(tmdbId, cancellationToken: cancellationToken)
                    : await tmdbService.GetMovieByIdAsync(tmdbId, cancellationToken: cancellationToken);

            if (lookup is null)
                return Result.Fail<BulkResolveResult>(
                    $"TMDB_ID_NOT_FOUND: The TMDB id {tmdbId} does not correspond to a known movie or TV show.");
        }

        // Apply the action to every item
        var resolvedAt = DateTime.UtcNow;
        var resolvedBy = currentUser.OktaId;

        foreach (var item in items)
        {
            switch (request.Action)
            {
                case ReviewResolutionAction.Assign:
                    item.Status = ReviewStatus.Resolved;
                    item.ResolvedTmdbId = request.TmdbId;
                    item.ResolvedKind = request.Kind;
                    item.ResolvedBy = resolvedBy;
                    item.ResolvedAt = resolvedAt;
                    break;

                case ReviewResolutionAction.Dismiss:
                    item.Status = ReviewStatus.Dismissed;
                    item.ResolvedBy = resolvedBy;
                    item.ResolvedAt = resolvedAt;
                    break;

                case ReviewResolutionAction.Delete:
                    // Remove underlying MediaFile(s)
                    var mediaFiles = await db.MediaFiles
                        .Where(mf => mf.FilePath == item.FilePath)
                        .ToListAsync(cancellationToken);

                    if (mediaFiles.Count > 0)
                        db.MediaFiles.RemoveRange(mediaFiles);

                    item.Status = ReviewStatus.Dismissed;
                    item.ResolvedBy = resolvedBy;
                    item.ResolvedAt = resolvedAt;
                    break;

                default:
                    return Result.Fail<BulkResolveResult>($"Unsupported bulk action: {request.Action}");
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new BulkResolveResult(items.Count));
    }
}

