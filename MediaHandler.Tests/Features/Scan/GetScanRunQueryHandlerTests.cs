#nullable enable
// GetScanRunQueryHandlerTests — Scan run lookup and review-item projection

using FluentAssertions;
using MediaHandler.Application.Common.DTOs;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Features.Scan.Queries.GetScanRun;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Enums;
using MediaHandler.Tests.Common;

namespace MediaHandler.Tests.Features.Scan;

public class GetScanRunQueryHandlerTests
{
    private readonly IApplicationDbContext _context = TestDbContext.Create();

    [Fact]
    public async Task Handle_ExistingScanRun_ReturnsMappedDto()
    {
        var run = new ScanRun
        {
            Mode = ScanMode.Full,
            Status = ScanStatus.Completed,
            LibraryRootIdsJson = "[]",
            StartedAt = DateTime.UtcNow.AddMinutes(-5),
            FinishedAt = DateTime.UtcNow,
            Added = 10,
            Updated = 2,
            Unchanged = 50,
            TotalDiscovered = 62
        };
        _context.ScanRuns.Add(run);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new GetScanRunQueryHandler(_context);
        var result = await handler.Handle(new GetScanRunQuery(run.Id, IncludeReview: false), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(run.Id);
        result.Value.Status.Should().Be(ScanStatus.Completed);
        result.Value.Counts.Added.Should().Be(10);
        result.Value.Counts.Updated.Should().Be(2);
        result.Value.Counts.Unchanged.Should().Be(50);
        result.Value.ReviewItems.Should().BeNull(because: "includeReview was false");
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsFailResult()
    {
        var handler = new GetScanRunQueryHandler(_context);
        var result = await handler.Handle(new GetScanRunQuery(Guid.NewGuid(), false), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("not found", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Handle_IncludeReviewTrue_WithOpenItems_ReturnsReviewItemList()
    {
        var run = new ScanRun
        {
            Mode = ScanMode.Full,
            Status = ScanStatus.Completed,
            LibraryRootIdsJson = "[]"
        };
        _context.ScanRuns.Add(run);

        var reviewItem = new ReviewItem
        {
            FilePath = "/nas/Movies/ambiguous.mkv",
            Reason = ReviewReason.NoTmdbResult,
            Status = ReviewStatus.Open,
            FirstSeenScanRunId = run.Id,
            CandidatesJson = "[]"
        };
        _context.ReviewItems.Add(reviewItem);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new GetScanRunQueryHandler(_context);
        var result = await handler.Handle(new GetScanRunQuery(run.Id, IncludeReview: true), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ReviewItems.Should().NotBeNull();
        result.Value.ReviewItems!.Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Handle_IncludeReviewTrue_NoOpenItems_ReturnsEmptyList()
    {
        var run = new ScanRun
        {
            Mode = ScanMode.Full,
            Status = ScanStatus.Completed,
            LibraryRootIdsJson = "[]"
        };
        _context.ScanRuns.Add(run);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new GetScanRunQueryHandler(_context);
        var result = await handler.Handle(new GetScanRunQuery(run.Id, IncludeReview: true), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ReviewItems.Should().NotBeNull().And.BeEmpty();
    }

    /// <summary>
    /// Verifies that when <c>includeReview=true</c> the handler returns only Open review items
    /// whose <c>FirstSeenScanRunId</c> matches the requested run id.
    /// Items belonging to other scan runs and items with non-Open status must be excluded.
    /// </summary>
    [Fact]
    public async Task IncludeReview_ReturnsOpenItemsForRun()
    {
        // Arrange: two runs, each with its own review items; some items are non-Open.
        var targetRun = new ScanRun
        {
            Mode = ScanMode.Full,
            Status = ScanStatus.Completed,
            LibraryRootIdsJson = "[]"
        };
        var otherRun = new ScanRun
        {
            Mode = ScanMode.Incremental,
            Status = ScanStatus.Completed,
            LibraryRootIdsJson = "[]"
        };
        _context.ScanRuns.AddRange(targetRun, otherRun);

        // Open item for the target run — must be included
        var openItemForTarget = new ReviewItem
        {
            FilePath = "/nas/Movies/ambiguous-target.mkv",
            Reason = ReviewReason.NoTmdbResult,
            Status = ReviewStatus.Open,
            FirstSeenScanRunId = targetRun.Id,
            CandidatesJson = "[]"
        };
        // Resolved item for the target run — must NOT be included (non-Open status)
        var resolvedItemForTarget = new ReviewItem
        {
            FilePath = "/nas/Movies/resolved-target.mkv",
            Reason = ReviewReason.YearMismatch,
            Status = ReviewStatus.Resolved,
            FirstSeenScanRunId = targetRun.Id,
            CandidatesJson = "[]"
        };
        // Open item for the OTHER run — must NOT be included (different run)
        var openItemForOtherRun = new ReviewItem
        {
            FilePath = "/nas/Movies/different-run.mkv",
            Reason = ReviewReason.MultipleCandidates,
            Status = ReviewStatus.Open,
            FirstSeenScanRunId = otherRun.Id,
            CandidatesJson = "[]"
        };
        _context.ReviewItems.AddRange(openItemForTarget, resolvedItemForTarget, openItemForOtherRun);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new GetScanRunQueryHandler(_context);

        // Act
        var result = await handler.Handle(
            new GetScanRunQuery(targetRun.Id, IncludeReview: true),
            TestContext.Current.CancellationToken);

        // Assert: only the Open item for the target run is returned
        result.IsSuccess.Should().BeTrue();
        result.Value.ReviewItems.Should().NotBeNull();

        var items = result.Value.ReviewItems!;
        items.Should().HaveCount(1, because: "only the single Open item for this run should be returned");
        items.Should().Contain(ri => ri.FilePath == openItemForTarget.FilePath,
            because: "the Open item should be present");
        items.Should().NotContain(ri => ri.FilePath == resolvedItemForTarget.FilePath,
            because: "Resolved items must not appear in the results");
        items.Should().NotContain(ri => ri.FilePath == openItemForOtherRun.FilePath,
            because: "items from other scan runs must not appear");
    }
}

