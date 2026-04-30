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
}

