using FluentAssertions;
using MediaHandler.Application.Common.Mappings;
using MediaHandler.Application.Features.Media.Commands.CreateMedia;
using MediaHandler.Application.Features.Media.Queries.GetMediaById;
using MediaHandler.Application.Features.Media.Queries.GetMediaList;
using MediaHandler.Domain.Enums;
using MediaHandler.IntegrationTests.Common;
using NSubstitute;
using MediaHandler.Application.Common.Interfaces;
using AutoMapper;

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
            TmdbId: 550,
            Title: "Fight Club",
            OriginalTitle: null,
            Overview: "An insomniac office worker...",
            Type: MediaType.Film,
            ReleaseDate: new DateTime(1999, 10, 15),
            Runtime: 139,
            PosterPath: null,
            BackdropPath: null,
            VoteAverage: 8.4m,
            VoteCount: 26000,
            Genres: ["Drama", "Thriller"],
            Language: "en");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var genres = DbContext.MediaGenres.Where(g => g.MediaId == result.Value).Select(g => g.Name).ToList();
        genres.Should().BeEquivalentTo(["Drama", "Thriller"]);
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
        result.Value.Genres.Should().BeEquivalentTo(["Crime", "Mystery"]);
    }

    [Fact]
    public async Task GetMediaList_FilterByGenre_ReturnsMatchingItems()
    {
        var handler = new CreateMediaCommandHandler(DbContext);
        await handler.Handle(new CreateMediaCommand(101, "Action Movie", null, null, MediaType.Film, null, null, null, null, null, null, ["Action"], "en"), CancellationToken.None);
        await handler.Handle(new CreateMediaCommand(102, "Drama Movie", null, null, MediaType.Film, null, null, null, null, null, null, ["Drama"], "en"), CancellationToken.None);

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
}
