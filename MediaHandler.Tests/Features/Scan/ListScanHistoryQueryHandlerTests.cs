// ListScanHistoryQueryHandlerTests — paginated scan-run history query

using FluentAssertions;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Features.Scan.Queries.ListScanHistory;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Enums;
using MediaHandler.Tests.Common;

namespace MediaHandler.Tests.Features.Scan;

public class ListScanHistoryQueryHandlerTests
{
    private readonly IApplicationDbContext _context = TestDbContext.Create();

    // Helpers

    private static ScanRun MakeScanRun(DateTime startedAt) => new()
    {
        Mode = ScanMode.Full,
        Status = ScanStatus.Completed,
        LibraryRootIdsJson = "[]",
        StartedAt = startedAt
    };

    // Tests

    /// <summary>
    ///     Returns a paginated page of scan runs ordered by StartedAt descending.
    /// </summary>
    [Fact]
    public async Task Handle_MultipleRuns_ReturnsPaginatedResultsOrderedByStartedAtDesc()
    {
        var oldest = MakeScanRun(DateTime.UtcNow.AddMinutes(-30));
        var middle = MakeScanRun(DateTime.UtcNow.AddMinutes(-20));
        var newest = MakeScanRun(DateTime.UtcNow.AddMinutes(-10));

        _context.ScanRuns.AddRange(oldest, middle, newest);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new ListScanHistoryQueryHandler(_context);
        var result = await handler.Handle(new ListScanHistoryQuery(Page: 1, PageSize: 10), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(3);
        result.Value.Items.Should().HaveCount(3);

        // Must be ordered newest → oldest
        result.Value.Items[0].StartedAt.Should().Be(newest.StartedAt);
        result.Value.Items[1].StartedAt.Should().Be(middle.StartedAt);
        result.Value.Items[2].StartedAt.Should().Be(oldest.StartedAt);
    }

    /// <summary>
    ///     When the database contains no scan runs the handler returns totalCount=0 and an empty list.
    /// </summary>
    [Fact]
    public async Task Handle_NoRuns_ReturnsEmptyPagedResult()
    {
        var handler = new ListScanHistoryQueryHandler(_context);
        var result = await handler.Handle(new ListScanHistoryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(0);
        result.Value.Items.Should().BeEmpty();
    }

    /// <summary>
    ///     Requesting a page number beyond the total range returns an empty item list — no error.
    /// </summary>
    [Fact]
    public async Task Handle_PageBeyondTotalRange_ReturnsEmptyItemsNoError()
    {
        _context.ScanRuns.Add(MakeScanRun(DateTime.UtcNow.AddMinutes(-5)));
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new ListScanHistoryQueryHandler(_context);
        // Only 1 run exists; requesting page 99 should yield 0 items but still succeed
        var result = await handler.Handle(new ListScanHistoryQuery(Page: 99, PageSize: 20), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(1);
        result.Value.Items.Should().BeEmpty();
    }

    /// <summary>
    ///     The validator rejects a pageSize greater than 100.
    /// </summary>
    [Fact]
    public void Validator_PageSizeGreaterThan100_IsInvalid()
    {
        var validator = new ListScanHistoryQueryValidator();
        var result = validator.Validate(new ListScanHistoryQuery(Page: 1, PageSize: 101));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(ListScanHistoryQuery.PageSize));
    }

    /// <summary>
    ///     The validator accepts pageSize of exactly 100.
    /// </summary>
    [Fact]
    public void Validator_PageSizeOf100_IsValid()
    {
        var validator = new ListScanHistoryQueryValidator();
        var result = validator.Validate(new ListScanHistoryQuery(Page: 1, PageSize: 100));

        result.IsValid.Should().BeTrue();
    }

    /// <summary>
    ///     The validator rejects page = 0.
    /// </summary>
    [Fact]
    public void Validator_PageZero_IsInvalid()
    {
        var validator = new ListScanHistoryQueryValidator();
        var result = validator.Validate(new ListScanHistoryQuery(Page: 0, PageSize: 20));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(ListScanHistoryQuery.Page));
    }
}

