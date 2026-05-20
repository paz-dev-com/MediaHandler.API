// BatchAssignReviewItems — command, validator, and handler for batch-assigning review items
// to a single target Media entity using the internal Media.Id (Guid).

using FluentValidation;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Review.Commands.BatchAssignReviewItems;

/// <summary>Result for a single item in the batch.</summary>
public record BatchAssignItemResult(
    Guid ReviewItemId,
    bool Success,
    string? ErrorMessage);

/// <summary>Response returned after a batch-assign operation.</summary>
public record BatchAssignReviewItemsResponse(
    IReadOnlyList<BatchAssignItemResult> Results);

/// <summary>Command to assign multiple <c>ReviewItem</c> rows to a single target <c>Media</c>.</summary>
public record BatchAssignReviewItemsCommand(
    Guid[] ReviewItemIds,
    Guid TargetMediaId) : IRequest<Result<BatchAssignReviewItemsResponse>>;

// =========================================================================
// Validator
// =========================================================================

public class BatchAssignReviewItemsCommandValidator : AbstractValidator<BatchAssignReviewItemsCommand>
{
    public BatchAssignReviewItemsCommandValidator()
    {
        RuleFor(x => x.ReviewItemIds)
            .NotEmpty().WithMessage("ReviewItemIds must not be empty.");

        RuleForEach(x => x.ReviewItemIds)
            .NotEqual(Guid.Empty).WithMessage("Each ReviewItemId must be a valid non-empty GUID.");

        RuleFor(x => x.TargetMediaId)
            .NotEqual(Guid.Empty).WithMessage("TargetMediaId must be a valid non-empty GUID.");
    }
}

// =========================================================================
// Handler
// =========================================================================

/// <summary>
///     Resolves all specified <c>ReviewItem</c> rows to the target <c>Media</c> entity.
///     Returns per-item success/failure results; a single SaveChangesAsync is called after the loop.
///     Returns <c>Result.Fail("MEDIA_NOT_FOUND")</c> when <see cref="BatchAssignReviewItemsCommand.TargetMediaId"/>
///     does not correspond to an existing <c>Media</c> record.
/// </summary>
public sealed class BatchAssignReviewItemsCommandHandler(IApplicationDbContext db)
    : IRequestHandler<BatchAssignReviewItemsCommand, Result<BatchAssignReviewItemsResponse>>
{
    public async Task<Result<BatchAssignReviewItemsResponse>> Handle(
        BatchAssignReviewItemsCommand request,
        CancellationToken cancellationToken)
    {
        // Resolve target media once — fail the entire batch if not found
        var media = await db.Medias
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == request.TargetMediaId, cancellationToken);

        if (media is null)
            return Result.Fail<BatchAssignReviewItemsResponse>("MEDIA_NOT_FOUND");

        var results = new List<BatchAssignItemResult>(request.ReviewItemIds.Length);

        foreach (var reviewItemId in request.ReviewItemIds)
        {
            var reviewItem = await db.ReviewItems
                .FirstOrDefaultAsync(r => r.Id == reviewItemId, cancellationToken);

            if (reviewItem is null)
            {
                results.Add(new BatchAssignItemResult(reviewItemId, false, "REVIEW_ITEM_NOT_FOUND"));
                continue;
            }

            reviewItem.ResolvedTmdbId = media.TmdbId;
            reviewItem.ResolvedKind = media.Type;
            reviewItem.Status = ReviewStatus.Resolved;
            reviewItem.ResolvedAt = DateTime.UtcNow;

            results.Add(new BatchAssignItemResult(reviewItemId, true, null));
        }

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new BatchAssignReviewItemsResponse(results.AsReadOnly()));
    }
}

