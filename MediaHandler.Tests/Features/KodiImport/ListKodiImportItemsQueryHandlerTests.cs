using FluentAssertions;
using MediaHandler.Application.Features.KodiImport.Queries.ListKodiImportItems;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Enums;
using MediaHandler.Tests.Common;

namespace MediaHandler.Tests.Features.KodiImport;

public class ListKodiImportItemsQueryHandlerTests
{
    private readonly TestDbContext _context = TestDbContext.Create();

    [Fact]
    public async Task ListItems_FiltersByOutcome()
    {
        var run = SeedRunWithOutcomes();
        var handler = new ListKodiImportItemsQueryHandler(_context);

        var result = await handler.Handle(
            new ListKodiImportItemsQuery(run.Id, ImportItemStatus.Created, null),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(3);
        result.Value.Items.Should().OnlyContain(i => i.Outcome == ImportItemStatus.Created);
    }

    [Fact]
    public async Task ListItems_FiltersByKind()
    {
        var run = SeedRunWithOutcomes();
        var handler = new ListKodiImportItemsQueryHandler(_context);

        var result = await handler.Handle(
            new ListKodiImportItemsQuery(run.Id, null, KodiItemKind.Episode),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(2);
        result.Value.Items.Should().OnlyContain(i => i.ItemKind == KodiItemKind.Episode);
    }

    [Fact]
    public async Task ListItems_RespectsPagination()
    {
        var run = SeedRunWithOutcomes();
        var handler = new ListKodiImportItemsQueryHandler(_context);

        var result = await handler.Handle(
            new ListKodiImportItemsQuery(run.Id, (ImportItemStatus?)null, (KodiItemKind?)null, 2, 2),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Page.Should().Be(2);
        result.Value.PageSize.Should().Be(2);
        result.Value.TotalCount.Should().Be(5);
        result.Value.TotalPages.Should().Be(3);
        result.Value.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListItems_MissingRun_ReturnsNotFound()
    {
        var handler = new ListKodiImportItemsQueryHandler(_context);
        var result = await handler.Handle(
            new ListKodiImportItemsQuery(Guid.NewGuid(), null, null), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainMatch("NOT_FOUND*");
    }

    [Fact]
    public void ListItems_InvalidPageSize_ValidatorRejects()
    {
        var validator = new ListKodiImportItemsQueryValidator();

        var result = validator.Validate(new ListKodiImportItemsQuery(Guid.NewGuid(), (ImportItemStatus?)null, (KodiItemKind?)null, 0, 20));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Page");
    }

    private ImportRun SeedRunWithOutcomes()
    {
        var run = new ImportRun
        {
            Mode = KodiImportMode.Import,
            Status = ImportRunStatus.Completed,
            SourceFileName = "MyVideos121.db",
            SchemaVersion = 121,
            StartedAt = DateTime.UtcNow
        };
        _context.ImportRuns.Add(run);
        _context.ImportItemOutcomes.AddRange(
            new ImportItemOutcome
            {
                ImportRunId = run.Id,
                KodiItemKind = KodiItemKind.Movie,
                KodiItemId = 1,
                Title = "Created Movie",
                MediaKind = MediaType.Film,
                Outcome = ImportItemStatus.Created
            },
            new ImportItemOutcome
            {
                ImportRunId = run.Id,
                KodiItemKind = KodiItemKind.Movie,
                KodiItemId = 2,
                Title = "Reused Movie",
                MediaKind = MediaType.Film,
                Outcome = ImportItemStatus.Reused
            },
            new ImportItemOutcome
            {
                ImportRunId = run.Id,
                KodiItemKind = KodiItemKind.TvShow,
                KodiItemId = 10,
                Title = "Created Show",
                MediaKind = MediaType.TvShow,
                Outcome = ImportItemStatus.Created
            },
            new ImportItemOutcome
            {
                ImportRunId = run.Id,
                KodiItemKind = KodiItemKind.Episode,
                KodiItemId = 100,
                Title = "Episode One",
                MediaKind = MediaType.TvShow,
                Outcome = ImportItemStatus.Created
            },
            new ImportItemOutcome
            {
                ImportRunId = run.Id,
                KodiItemKind = KodiItemKind.Episode,
                KodiItemId = 101,
                Title = "Episode Two",
                MediaKind = MediaType.TvShow,
                Outcome = ImportItemStatus.Unchanged
            });
        _context.SaveChanges();
        return run;
    }
}
