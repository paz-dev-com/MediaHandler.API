using FluentAssertions;
using MediaHandler.Application.Features.KodiImport.Queries.GetKodiImportRun;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Enums;
using MediaHandler.Tests.Common;

namespace MediaHandler.Tests.Features.KodiImport;

public class GetKodiImportRunQueryHandlerTests
{
    private readonly TestDbContext _context = TestDbContext.Create();

    [Fact]
    public async Task GetRun_Existing_ReturnsDetailWithCountersAndPrefixes()
    {
        var run = new ImportRun
        {
            Mode = KodiImportMode.Import,
            Status = ImportRunStatus.Completed,
            SourceFileName = "MyVideos121.db",
            SchemaVersion = 121,
            StartedAt = DateTime.UtcNow.AddMinutes(-5),
            FinishedAt = DateTime.UtcNow,
            MoviesCreated = 3,
            ShowsCreated = 1,
            FilesLinked = 4,
            UnmatchedPrefixesJson = "[\"smb://FREEBOX/Unknown\"]"
        };
        _context.ImportRuns.Add(run);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new GetKodiImportRunQueryHandler(_context);
        var result = await handler.Handle(new GetKodiImportRunQuery(run.Id), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(run.Id);
        result.Value.Mode.Should().Be(KodiImportMode.Import);
        result.Value.Status.Should().Be(ImportRunStatus.Completed);
        result.Value.SourceFileName.Should().Be("MyVideos121.db");
        result.Value.SchemaVersion.Should().Be(121);
        result.Value.Counts.MoviesCreated.Should().Be(3);
        result.Value.Counts.ShowsCreated.Should().Be(1);
        result.Value.Counts.FilesLinked.Should().Be(4);
        result.Value.UnmatchedPrefixes.Should().ContainSingle()
            .Which.Should().Be("smb://FREEBOX/Unknown");
    }

    [Fact]
    public async Task GetRun_Missing_ReturnsNotFound()
    {
        var handler = new GetKodiImportRunQueryHandler(_context);
        var result = await handler.Handle(
            new GetKodiImportRunQuery(Guid.NewGuid()), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainMatch("NOT_FOUND*");
    }
}
