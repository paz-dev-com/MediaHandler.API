using FluentAssertions;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Features.Media.Commands.CreateMedia;
using MediaHandler.Application.Features.Media.Queries.GetMediaById;
using MediaHandler.Application.Features.Media.Queries.GetMediaList;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Enums;
using MediaHandler.IntegrationTests.Common;
using NSubstitute;

namespace MediaHandler.IntegrationTests.Features.Media;

public class MediaIntegrationTests : IntegrationTestBase
{
    private ICurrentUserService CurrentUser()
    {
        var svc = Substitute.For<ICurrentUserService>();
        svc.OktaId.Returns((string?)null);
        return svc;
    }

    [Fact]
    public async Task CreateMedia_PersistsGenres_ToJoinTable()
    {
        var handler = new CreateMediaCommandHandler(DbContext);
        var command = new CreateMediaCommand(
            550,
            "Fight Club",
            null,
            "An insomniac office worker...",
            MediaType.Film,
            new DateTime(1999, 10, 15),
            139,
            null,
            null,
            8.4m,
            26000,
            ["Drama", "Thriller"],
            "en");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var genres = DbContext.MediaGenres.Where(g => g.MediaId == result.Value).Select(g => g.Name).ToList();
        genres.Should().BeEquivalentTo("Drama", "Thriller");
    }

    [Fact]
    public async Task GetMediaById_IncludesGenres_InDto()
    {
        var createHandler = new CreateMediaCommandHandler(DbContext);
        var createResult = await createHandler.Handle(new CreateMediaCommand(
            551, "Se7en", null, null, MediaType.Film,
            null, null, null, null, null, null,
            ["Crime", "Mystery"], "en"), CancellationToken.None);

        var queryHandler = new GetMediaByIdQueryHandler(DbContext, CurrentUser());
        var result = await queryHandler.Handle(new GetMediaByIdQuery(createResult.Value), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Genres.Should().BeEquivalentTo("Crime", "Mystery");
    }

    [Fact]
    public async Task GetMediaList_FilterByGenre_ReturnsMatchingItems()
    {
        var handler = new CreateMediaCommandHandler(DbContext);
        await handler.Handle(
            new CreateMediaCommand(101, "Action Movie", null, null, MediaType.Film, null, null, null, null, null, null,
                ["Action"], "en"), CancellationToken.None);
        await handler.Handle(
            new CreateMediaCommand(102, "Drama Movie", null, null, MediaType.Film, null, null, null, null, null, null,
                ["Drama"], "en"), CancellationToken.None);

        var queryHandler = new GetMediaListQueryHandler(DbContext, CurrentUser());
        var result = await queryHandler.Handle(new GetMediaListQuery(Genre: "Action"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(1);
        result.Value.Items.First().Title.Should().Be("Action Movie");
    }

    [Fact]
    public async Task GetMediaById_NonExistentId_ReturnsFailResult()
    {
        var handler = new GetMediaByIdQueryHandler(DbContext, CurrentUser());
        var result = await handler.Handle(new GetMediaByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain("Media not found.");
    }

    [Fact]
    public async Task GetMediaList_TvShowOwnedSeasonCount_UsesActualOwnedSeasons()
    {
        var media = new Domain.Entities.Media
        {
            TmdbId = 110492,
            Title = "Peacemaker",
            Type = MediaType.TvShow,
            NumberOfSeasons = 2
        };
        DbContext.Medias.Add(media);
        await DbContext.SaveChangesAsync(CancellationToken.None);

        DbContext.TvSeasons.AddRange(
            new TvSeason { MediaId = media.Id, SeasonNumber = 1, Name = "Season 1", EpisodeCount = 8 },
            new TvSeason { MediaId = media.Id, SeasonNumber = 2, Name = "Season 2", EpisodeCount = 8 });

        for (var ep = 1; ep <= 8; ep++)
            DbContext.MediaFiles.Add(new MediaFile
            {
                MediaId = media.Id,
                FilePath = $"/nas/tv/Peacemaker/Season 01/Peacemaker.S01E{ep:D2}.mkv",
                Fingerprint = $"fp_s01e{ep:D2}"
            });

        await DbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetMediaListQueryHandler(DbContext, CurrentUser());
        var result = await handler.Handle(new GetMediaListQuery(Page: 1, PageSize: 20), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var item = result.Value.Items.Single(i => i.Id == media.Id);
        item.NumberOfSeasons.Should().Be(2);
        item.OwnedSeasonCount.Should().Be(1);
    }
}