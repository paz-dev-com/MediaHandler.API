using FluentAssertions;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Features.Media.Commands.LinkMediaFile;
using MediaHandler.Application.Features.Media.Commands.UnlinkMediaFile;
using MediaHandler.Application.Features.Media.Queries.GetMediaById;
using MediaHandler.Application.Features.Media.Queries.GetMediaCompleteness;
using MediaHandler.Application.Features.Media.Queries.GetUnlinkedFiles;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Enums;
using MediaHandler.IntegrationTests.Common;
using NSubstitute;

namespace MediaHandler.IntegrationTests.Features.Media;

public class MediaFileLinkingIntegrationTests : IntegrationTestBase
{
    private ICurrentUserService CurrentUser()
    {
        var svc = Substitute.For<ICurrentUserService>();
        svc.OktaId.Returns((string?)null);
        return svc;
    }

    [Fact]
    public async Task FullLinkWorkflow_LinkVerifyInDetailAndUnlinkedFiles_ThenUnlink()
    {
        // Seed media + unlinked file
        var media = new Domain.Entities.Media { TmdbId = 9100, Title = "Breaking Bad", Type = MediaType.TvShow };
        var file = new MediaFile { FilePath = "/nas/bb/s01e01.mkv", Fingerprint = "fp_bb_1" };
        DbContext.Medias.Add(media);
        DbContext.MediaFiles.Add(file);
        await DbContext.SaveChangesAsync(CancellationToken.None);

        // File should be in unlinked list before linking
        var unlinkedHandler = new GetUnlinkedFilesQueryHandler(DbContext);
        var beforeLink = await unlinkedHandler.Handle(
            new GetUnlinkedFilesQuery(1, 20), CancellationToken.None);
        beforeLink.Value.Items.Should().ContainSingle(f => f.Id == file.Id);

        // Link file
        var linkHandler = new LinkMediaFileCommandHandler(DbContext);
        var linkResult = await linkHandler.Handle(
            new LinkMediaFileCommand(media.Id, file.Id), CancellationToken.None);
        linkResult.IsSuccess.Should().BeTrue();

        // GET media/{id} should include the file
        var getHandler = new GetMediaByIdQueryHandler(DbContext, CurrentUser());
        var mediaDto = await getHandler.Handle(new GetMediaByIdQuery(media.Id), CancellationToken.None);
        mediaDto.IsSuccess.Should().BeTrue();
        mediaDto.Value.Files.Should().ContainSingle(f => f.Id == file.Id);

        // File should NOT appear in unlinked list
        var afterLink = await unlinkedHandler.Handle(
            new GetUnlinkedFilesQuery(1, 20), CancellationToken.None);
        afterLink.Value.Items.Should().NotContain(f => f.Id == file.Id);

        // Unlink file
        var unlinkHandler = new UnlinkMediaFileCommandHandler(DbContext);
        var unlinkResult = await unlinkHandler.Handle(
            new UnlinkMediaFileCommand(media.Id, file.Id), CancellationToken.None);
        unlinkResult.IsSuccess.Should().BeTrue();

        // File should appear in unlinked list again
        var afterUnlink = await unlinkedHandler.Handle(
            new GetUnlinkedFilesQuery(1, 20), CancellationToken.None);
        afterUnlink.Value.Items.Should().ContainSingle(f => f.Id == file.Id);
    }

    [Fact]
    public async Task LinkFile_WhenAlreadyLinkedToDifferentMedia_Returns422WithFileAlreadyLinked()
    {
        var media1 = new Domain.Entities.Media { TmdbId = 9101, Title = "Media 1", Type = MediaType.Film };
        var media2 = new Domain.Entities.Media { TmdbId = 9102, Title = "Media 2", Type = MediaType.Film };
        var file = new MediaFile { FilePath = "/nas/file.mkv", Fingerprint = "fp_double", MediaId = null };
        DbContext.Medias.AddRange(media1, media2);
        DbContext.MediaFiles.Add(file);
        await DbContext.SaveChangesAsync(CancellationToken.None);

        var linkHandler = new LinkMediaFileCommandHandler(DbContext);

        // Link to media1 first
        var result1 = await linkHandler.Handle(
            new LinkMediaFileCommand(media1.Id, file.Id), CancellationToken.None);
        result1.IsSuccess.Should().BeTrue();

        // Attempt link to media2 — should fail with FILE_ALREADY_LINKED
        var result2 = await linkHandler.Handle(
            new LinkMediaFileCommand(media2.Id, file.Id), CancellationToken.None);
        result2.IsSuccess.Should().BeFalse();
        result2.Errors.Should().ContainMatch("FILE_ALREADY_LINKED*");
    }

    [Fact]
    public async Task GetCompleteness_TvShow_ReturnsAccurateSeasonData()
    {
        var media = new Domain.Entities.Media { TmdbId = 9103, Title = "Test Show", Type = MediaType.TvShow };
        DbContext.Medias.Add(media);
        await DbContext.SaveChangesAsync(CancellationToken.None);

        var season = new TvSeason { MediaId = media.Id, SeasonNumber = 1, Name = "Season 1", EpisodeCount = 3 };
        DbContext.TvSeasons.Add(season);
        await DbContext.SaveChangesAsync(CancellationToken.None);

        var ep1 = new TvEpisode { SeasonId = season.Id, EpisodeNumber = 1, Name = "Pilot" };
        var ep2 = new TvEpisode { SeasonId = season.Id, EpisodeNumber = 2, Name = "Episode 2" };
        var ep3 = new TvEpisode { SeasonId = season.Id, EpisodeNumber = 3, Name = "Episode 3" };
        DbContext.TvEpisodes.AddRange(ep1, ep2, ep3);
        await DbContext.SaveChangesAsync(CancellationToken.None);

        // Link files for ep1 and ep3 only
        var file1 = new MediaFile { FilePath = "/nas/s01e01.mkv", Fingerprint = "s1e1", MediaId = media.Id };
        var file3 = new MediaFile { FilePath = "/nas/s01e03.mkv", Fingerprint = "s1e3", MediaId = media.Id };
        DbContext.MediaFiles.AddRange(file1, file3);
        await DbContext.SaveChangesAsync(CancellationToken.None);

        DbContext.EpisodeFileLinks.AddRange(
            new EpisodeFileLink { TvEpisodeId = ep1.Id, MediaFileId = file1.Id },
            new EpisodeFileLink { TvEpisodeId = ep3.Id, MediaFileId = file3.Id });
        await DbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetMediaCompletenessQueryHandler(DbContext);
        var result = await handler.Handle(
            new GetMediaCompletenessQuery(media.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        var dto = result.Value[0];
        dto.TotalExpected.Should().Be(3);
        dto.OwnedCount.Should().Be(2);
        dto.MissingEpisodeNumbers.Should().BeEquivalentTo(new[] { 2 });
        dto.IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task GetUnlinkedFiles_Pagination_ReturnsCorrectPage()
    {
        // Seed 25 unlinked files
        for (var i = 1; i <= 25; i++)
            DbContext.MediaFiles.Add(new MediaFile
            {
                FilePath = $"/nas/unlinked/file{i:D3}.mkv",
                Fingerprint = $"uf_pag_{i}"
            });
        await DbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetUnlinkedFilesQueryHandler(DbContext);

        // Page 2 with pageSize 10 → items 11-20
        var result = await handler.Handle(
            new GetUnlinkedFilesQuery(2, 10), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(10);
        result.Value.TotalCount.Should().Be(25);
        result.Value.Page.Should().Be(2);
        result.Value.TotalPages.Should().Be(3);
    }
}

