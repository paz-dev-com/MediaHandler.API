#nullable enable
// ResolveReviewItemCommandHandlerTests — unit tests for the admin review-item resolution workflow.
// These tests must FAIL before the command handler is implemented.

using FluentAssertions;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Features.Review.Commands.ResolveReviewItem;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Enums;
using MediaHandler.Tests.Common;
using NSubstitute;

namespace MediaHandler.Tests.Features.Review;

/// <summary>
/// Unit tests for <c>ResolveReviewItemCommandHandler</c>.
/// Covers: Assign, Dismiss, Delete actions and error conditions.
/// </summary>
public class ResolveReviewItemCommandHandlerTests
{
    private readonly IApplicationDbContext _db = TestDbContext.Create();
    private readonly ITmdbService _tmdbService = Substitute.For<ITmdbService>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();

    private ResolveReviewItemCommandHandler CreateHandler() =>
        new(_db, _tmdbService, _currentUser);

    private async Task<ReviewItem> AddOpenReviewItem(string? filePath = null)
    {
        var item = new ReviewItem
        {
            FilePath = filePath ?? "/nas/Movies/SomeMovie.mkv",
            Reason = ReviewReason.NoTmdbResult,
            Status = ReviewStatus.Open,
            ParsedTitle = "Some Movie",
            ParsedYear = 2020
        };
        _db.ReviewItems.Add(item);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return item;
    }

    // =========================================================================
    // Assign: valid TMDB id → Status = Resolved, fields populated
    // =========================================================================

    [Fact]
    public async Task Handle_Assign_ValidTmdbId_SetsStatusResolved()
    {
        _currentUser.OktaId.Returns("user-123");

        // Stub TMDB lookup to return a valid result
        _tmdbService.GetMovieByIdAsync(27205, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new TmdbIdLookupResult(27205, MediaType.Film, "Inception", 2010, null));

        var item = await AddOpenReviewItem();

        var command = new ResolveReviewItemCommand(
            item.Id,
            ReviewResolutionAction.Assign,
            TmdbId: 27205,
            Kind: MediaType.Film);

        var handler = CreateHandler();
        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();

        var refreshed = await _db.ReviewItems.FindAsync([item.Id], TestContext.Current.CancellationToken);
        refreshed!.Status.Should().Be(ReviewStatus.Resolved);
        refreshed.ResolvedTmdbId.Should().Be(27205);
        refreshed.ResolvedKind.Should().Be(MediaType.Film);
        refreshed.ResolvedBy.Should().Be("user-123");
        refreshed.ResolvedAt.Should().NotBeNull();
        refreshed.ResolvedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    // =========================================================================
    // Assign: TMDB id not found → UnprocessableEntity(TMDB_ID_NOT_FOUND)
    // =========================================================================

    [Fact]
    public async Task Handle_Assign_TmdbIdNotFound_ReturnsUnprocessableEntity()
    {
        _currentUser.OktaId.Returns("user-123");

        // Stub both movie and TV show lookups to return null
        _tmdbService.GetMovieByIdAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((TmdbIdLookupResult?)null);
        _tmdbService.GetTvShowByIdAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((TmdbIdLookupResult?)null);

        var item = await AddOpenReviewItem();

        var command = new ResolveReviewItemCommand(
            item.Id,
            ReviewResolutionAction.Assign,
            TmdbId: 99999999,
            Kind: MediaType.Film);

        var handler = CreateHandler();
        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TMDB_ID_NOT_FOUND"));
    }

    // =========================================================================
    // Dismiss: marks item Dismissed
    // =========================================================================

    [Fact]
    public async Task Handle_Dismiss_SetsStatusDismissed()
    {
        _currentUser.OktaId.Returns("user-456");

        var item = await AddOpenReviewItem();

        var command = new ResolveReviewItemCommand(
            item.Id,
            ReviewResolutionAction.Dismiss,
            TmdbId: null,
            Kind: null);

        var handler = CreateHandler();
        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();

        var refreshed = await _db.ReviewItems.FindAsync([item.Id], TestContext.Current.CancellationToken);
        refreshed!.Status.Should().Be(ReviewStatus.Dismissed);
    }

    // =========================================================================
    // Delete: removes underlying MediaFile, marks ReviewItem Dismissed
    // =========================================================================

    [Fact]
    public async Task Handle_Delete_RemovesMediaFile_AndMarksItemDismissed()
    {
        _currentUser.OktaId.Returns("user-789");

        // Create a MediaFile that the review item refers to
        var mediaFile = new MediaFile
        {
            FilePath = "/nas/Movies/SomeMovie.mkv",
            Fingerprint = "abc123",
            MtimeUtc = DateTime.UtcNow,
            FileSizeBytes = 1_000_000,
            Role = MediaFileRole.Main
        };
        _db.MediaFiles.Add(mediaFile);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var item = await AddOpenReviewItem("/nas/Movies/SomeMovie.mkv");

        var command = new ResolveReviewItemCommand(
            item.Id,
            ReviewResolutionAction.Delete,
            TmdbId: null,
            Kind: null);

        var handler = CreateHandler();
        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();

        // MediaFile should be gone
        var file = await _db.MediaFiles.FindAsync([mediaFile.Id], TestContext.Current.CancellationToken);
        file.Should().BeNull();

        // ReviewItem should be Dismissed
        var refreshed = await _db.ReviewItems.FindAsync([item.Id], TestContext.Current.CancellationToken);
        refreshed!.Status.Should().Be(ReviewStatus.Dismissed);
    }

    // =========================================================================
    // Non-Open item → Conflict(REVIEW_ALREADY_RESOLVED)
    // =========================================================================

    [Theory]
    [InlineData(ReviewStatus.Resolved)]
    [InlineData(ReviewStatus.Dismissed)]
    public async Task Handle_NonOpenItem_ReturnsConflict(ReviewStatus status)
    {
        _currentUser.OktaId.Returns("user-123");

        var item = new ReviewItem
        {
            FilePath = "/nas/Movies/SomeMovie.mkv",
            Reason = ReviewReason.NoTmdbResult,
            Status = status,
            ParsedTitle = "Some Movie"
        };
        _db.ReviewItems.Add(item);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new ResolveReviewItemCommand(
            item.Id,
            ReviewResolutionAction.Dismiss,
            TmdbId: null,
            Kind: null);

        var handler = CreateHandler();
        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("REVIEW_ALREADY_RESOLVED"));
    }

    // =========================================================================
    // ReviewItem not found → error
    // =========================================================================

    [Fact]
    public async Task Handle_ReviewItemNotFound_ReturnsFail()
    {
        _currentUser.OktaId.Returns("user-123");

        var command = new ResolveReviewItemCommand(
            Guid.NewGuid(), // non-existent
            ReviewResolutionAction.Dismiss,
            TmdbId: null,
            Kind: null);

        var handler = CreateHandler();
        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
    }

    // =========================================================================
    // Validator: Assign without TmdbId → validation failure
    // =========================================================================

    [Fact]
    public void Validator_Assign_WithoutTmdbId_FailsValidation()
    {
        var validator = new ResolveReviewItemCommandValidator();
        var command = new ResolveReviewItemCommand(
            Guid.NewGuid(),
            ReviewResolutionAction.Assign,
            TmdbId: null,    // missing — required for Assign
            Kind: MediaType.Film);

        var result = validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.TmdbId));
    }

    // =========================================================================
    // Validator: Assign without Kind → validation failure
    // =========================================================================

    [Fact]
    public void Validator_Assign_WithoutKind_FailsValidation()
    {
        var validator = new ResolveReviewItemCommandValidator();
        var command = new ResolveReviewItemCommand(
            Guid.NewGuid(),
            ReviewResolutionAction.Assign,
            TmdbId: 27205,
            Kind: null);  // missing — required for Assign

        var result = validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.Kind));
    }

    // =========================================================================
    // Validator: Dismiss with no TmdbId → valid (TmdbId not required for Dismiss)
    // =========================================================================

    [Fact]
    public void Validator_Dismiss_WithoutTmdbId_PassesValidation()
    {
        var validator = new ResolveReviewItemCommandValidator();
        var command = new ResolveReviewItemCommand(
            Guid.NewGuid(),
            ReviewResolutionAction.Dismiss,
            TmdbId: null,
            Kind: null);

        var result = validator.Validate(command);

        result.IsValid.Should().BeTrue();
    }
}

