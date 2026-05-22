using FluentAssertions;
using MediaHandler.Application.Features.Media.Queries.GetUnlinkedFiles;
using MediaHandler.Domain.Enums;
using MediaHandler.Tests.Common;

namespace MediaHandler.Tests.Features.Media;

public class GetUnlinkedFilesQueryHandlerTests
{
    private readonly TestDbContext _context = TestDbContext.Create();

    private GetUnlinkedFilesQueryHandler CreateHandler() => new(_context);

    [Fact]
    public async Task GetUnlinkedFiles_ReturnsOnlyFilesWithNullMediaId()
    {
        var media = new Domain.Entities.Media { TmdbId = 300, Title = "Test", Type = MediaType.Film };
        _context.Medias.Add(media);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        _context.MediaFiles.Add(new Domain.Entities.MediaFile
        { FilePath = "/linked/file.mkv", Fingerprint = "lf1", MediaId = media.Id });
        _context.MediaFiles.Add(new Domain.Entities.MediaFile
        { FilePath = "/unlinked/file.mkv", Fingerprint = "uf1" });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateHandler().Handle(
            new GetUnlinkedFilesQuery(1, 20), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(1);
        result.Value.Items[0].FilePath.Should().Be("/unlinked/file.mkv");
    }

    [Fact]
    public async Task GetUnlinkedFiles_RespectsPagination()
    {
        for (var i = 1; i <= 5; i++)
            _context.MediaFiles.Add(new Domain.Entities.MediaFile
            { FilePath = $"/unlinked/file{i:D2}.mkv", Fingerprint = $"uf{i}" });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateHandler().Handle(
            new GetUnlinkedFilesQuery(1, 2), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(2);
        result.Value.TotalCount.Should().Be(5);
    }

    [Fact]
    public async Task GetUnlinkedFiles_WhenNoUnlinkedFiles_ReturnsEmptyPagedResult()
    {
        var media = new Domain.Entities.Media { TmdbId = 301, Title = "Test", Type = MediaType.Film };
        _context.Medias.Add(media);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        _context.MediaFiles.Add(new Domain.Entities.MediaFile
        { FilePath = "/linked/file.mkv", Fingerprint = "lf2", MediaId = media.Id });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateHandler().Handle(
            new GetUnlinkedFilesQuery(1, 20), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().BeEmpty();
        result.Value.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetUnlinkedFiles_IsOrderedByFilePath()
    {
        _context.MediaFiles.Add(new Domain.Entities.MediaFile
        { FilePath = "/z/file.mkv", Fingerprint = "zf" });
        _context.MediaFiles.Add(new Domain.Entities.MediaFile
        { FilePath = "/a/file.mkv", Fingerprint = "af" });
        _context.MediaFiles.Add(new Domain.Entities.MediaFile
        { FilePath = "/m/file.mkv", Fingerprint = "mf" });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateHandler().Handle(
            new GetUnlinkedFilesQuery(1, 20), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Select(f => f.FilePath).Should().BeInAscendingOrder();
    }
}

