using FluentAssertions;
using MediaHandler.Application.Features.KodiImport.DTOs;
using MediaHandler.Application.Features.KodiImport.Queries.ListKodiImportHistory;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Enums;
using MediaHandler.Tests.Common;

namespace MediaHandler.Tests.Features.KodiImport;

public class ListKodiImportHistoryQueryHandlerTests
{
    private readonly TestDbContext _context = TestDbContext.Create();

    [Fact]
    public async Task ListHistory_ReturnsNewestFirst()
    {
        _context.ImportRuns.AddRange(
            new ImportRun
            {
                Mode = KodiImportMode.Import,
                Status = ImportRunStatus.Completed,
                SourceFileName = "MyVideos119.db",
                SchemaVersion = 119,
                StartedAt = DateTime.UtcNow.AddHours(-2)
            },
            new ImportRun
            {
                Mode = KodiImportMode.Import,
                Status = ImportRunStatus.Completed,
                SourceFileName = "MyVideos121.db",
                SchemaVersion = 121,
                StartedAt = DateTime.UtcNow.AddHours(-1)
            },
            new ImportRun
            {
                Mode = KodiImportMode.Preview,
                Status = ImportRunStatus.Completed,
                SourceFileName = "MyVideos131.db",
                SchemaVersion = 131,
                StartedAt = DateTime.UtcNow
            });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new ListKodiImportHistoryQueryHandler(_context);
        var result = await handler.Handle(
            new ListKodiImportHistoryQuery(), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(3);
        result.Value.Items.Select(r => r.SchemaVersion)
            .Should().Equal(131, 121, 119);
    }

    [Fact]
    public async Task ListHistory_RespectsPagination()
    {
        for (var i = 0; i < 5; i++)
        {
            _context.ImportRuns.Add(new ImportRun
            {
                Mode = KodiImportMode.Import,
                Status = ImportRunStatus.Completed,
                SourceFileName = $"MyVideos{i}.db",
                SchemaVersion = 100 + i,
                StartedAt = DateTime.UtcNow.AddMinutes(-i)
            });
        }

        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new ListKodiImportHistoryQueryHandler(_context);
        var result = await handler.Handle(
            new ListKodiImportHistoryQuery(2, 2), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Page.Should().Be(2);
        result.Value.PageSize.Should().Be(2);
        result.Value.TotalCount.Should().Be(5);
        result.Value.TotalPages.Should().Be(3);
        result.Value.Items.Should().HaveCount(2);
        // Newest first, page 2 => items 3 and 4 (0-based indices 2 and 3)
        result.Value.Items.Select(r => r.SchemaVersion)
            .Should().Equal(102, 103);
    }

    [Fact]
    public async Task ListHistory_Empty_ReturnsEmptyPagedResult()
    {
        var handler = new ListKodiImportHistoryQueryHandler(_context);
        var result = await handler.Handle(
            new ListKodiImportHistoryQuery(), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().BeEmpty();
        result.Value.TotalCount.Should().Be(0);
        result.Value.TotalPages.Should().Be(0);
    }

    [Fact]
    public void ListHistory_InvalidPageSize_ValidatorRejects()
    {
        var validator = new ListKodiImportHistoryQueryValidator();

        var result = validator.Validate(new ListKodiImportHistoryQuery(0, 20));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Page");
    }
}
