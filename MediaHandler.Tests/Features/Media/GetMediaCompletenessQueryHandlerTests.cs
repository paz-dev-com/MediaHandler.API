using FluentAssertions;
using MediaHandler.Application.Features.Media.Queries.GetMediaCompleteness;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Enums;
using MediaHandler.Tests.Common;

namespace MediaHandler.Tests.Features.Media;

public class GetMediaCompletenessQueryHandlerTests
{
    private readonly TestDbContext _context = TestDbContext.Create();

    private GetMediaCompletenessQueryHandler CreateHandler() => new(_context);

    private async Task<Domain.Entities.Media> SeedTvShowAsync(int tmdbId = 500)
    {
        var media = new Domain.Entities.Media { TmdbId = tmdbId, Title = "Test Show", Type = MediaType.TvShow };
        _context.Medias.Add(media);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return media;
    }

    private async Task<TvSeason> SeedSeasonAsync(Guid mediaId, int seasonNumber = 1, string name = "Season 1", int? episodeCount = null)
    {
        var season = new TvSeason
        {
            MediaId = mediaId,
            SeasonNumber = seasonNumber,
            Name = name,
            EpisodeCount = episodeCount
        };
        _context.TvSeasons.Add(season);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return season;
    }

    private async Task<TvEpisode> SeedEpisodeAsync(Guid seasonId, int episodeNumber, string name = "Episode")
    {
        var episode = new TvEpisode { SeasonId = seasonId, EpisodeNumber = episodeNumber, Name = $"{name} {episodeNumber}" };
        _context.TvEpisodes.Add(episode);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return episode;
    }

    private async Task LinkEpisodeFileAsync(Guid mediaId, Guid episodeId)
    {
        var file = new MediaFile { FilePath = $"/file_{episodeId}.mkv", Fingerprint = $"fp_{episodeId}", MediaId = mediaId };
        _context.MediaFiles.Add(file);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var link = new EpisodeFileLink { TvEpisodeId = episodeId, MediaFileId = file.Id };
        _context.EpisodeFileLinks.Add(link);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetCompleteness_TvShowWithMissingEpisodes_ReturnsCorrectMissingList()
    {
        var media = await SeedTvShowAsync(501);
        var season = await SeedSeasonAsync(media.Id, 1, "Season 1", 5);
        var ep1 = await SeedEpisodeAsync(season.Id, 1);
        var ep3 = await SeedEpisodeAsync(season.Id, 3);
        var ep5 = await SeedEpisodeAsync(season.Id, 5);
        await SeedEpisodeAsync(season.Id, 2);
        await SeedEpisodeAsync(season.Id, 4);

        await LinkEpisodeFileAsync(media.Id, ep1.Id);
        await LinkEpisodeFileAsync(media.Id, ep3.Id);
        await LinkEpisodeFileAsync(media.Id, ep5.Id);

        var result = await CreateHandler().Handle(
            new GetMediaCompletenessQuery(media.Id), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        var dto = result.Value[0];
        dto.TotalExpected.Should().Be(5);
        dto.OwnedCount.Should().Be(3);
        dto.MissingEpisodeNumbers.Should().BeEquivalentTo([2, 4]);
        dto.IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task GetCompleteness_TvShowWithCompleteSeasons_ReturnsIsCompleteTrue()
    {
        var media = await SeedTvShowAsync(502);
        var season = await SeedSeasonAsync(media.Id, 1, "Season 1", 3);
        var ep1 = await SeedEpisodeAsync(season.Id, 1);
        var ep2 = await SeedEpisodeAsync(season.Id, 2);
        var ep3 = await SeedEpisodeAsync(season.Id, 3);

        await LinkEpisodeFileAsync(media.Id, ep1.Id);
        await LinkEpisodeFileAsync(media.Id, ep2.Id);
        await LinkEpisodeFileAsync(media.Id, ep3.Id);

        var result = await CreateHandler().Handle(
            new GetMediaCompletenessQuery(media.Id), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value[0].IsComplete.Should().BeTrue();
        result.Value[0].MissingEpisodeNumbers.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCompleteness_TvShowWithSeason0_ExcludesSeason0()
    {
        var media = await SeedTvShowAsync(503);
        await SeedSeasonAsync(media.Id, 0, "Season 0", 2);
        await SeedSeasonAsync(media.Id, 1, "Season 1", 1);

        var result = await CreateHandler().Handle(
            new GetMediaCompletenessQuery(media.Id), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value[0].SeasonNumber.Should().Be(1);
    }

    [Fact]
    public async Task GetCompleteness_TvShowWithSpecialsSeason_ExcludesSpecialsSeason()
    {
        var media = await SeedTvShowAsync(504);
        await SeedSeasonAsync(media.Id, 1, "SPECIALS", 2);
        await SeedSeasonAsync(media.Id, 2, "Season 2", 1);

        var result = await CreateHandler().Handle(
            new GetMediaCompletenessQuery(media.Id), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value[0].SeasonNumber.Should().Be(2);
    }

    [Fact]
    public async Task GetCompleteness_WhenEpisodeCountIsNull_FallsBackToTvEpisodeRowCount()
    {
        var media = await SeedTvShowAsync(505);
        var season = await SeedSeasonAsync(media.Id);
        await SeedEpisodeAsync(season.Id, 1);
        await SeedEpisodeAsync(season.Id, 2);
        await SeedEpisodeAsync(season.Id, 3);
        await SeedEpisodeAsync(season.Id, 4);

        var result = await CreateHandler().Handle(
            new GetMediaCompletenessQuery(media.Id), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value[0].TotalExpected.Should().Be(4);
    }

    [Fact]
    public async Task GetCompleteness_ForFilmMediaType_ReturnsBadRequest()
    {
        var film = new Domain.Entities.Media { TmdbId = 506, Title = "Test Film", Type = MediaType.Film };
        _context.Medias.Add(film);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateHandler().Handle(
            new GetMediaCompletenessQuery(film.Id), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Errors.First().Should().StartWith("MEDIA_NOT_TV_SHOW");
    }

    [Fact]
    public async Task GetCompleteness_WhenMediaNotFound_ReturnsNotFound()
    {
        var result = await CreateHandler().Handle(
            new GetMediaCompletenessQuery(Guid.NewGuid()), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Errors.First().Should().StartWith("NOT_FOUND");
    }

    [Fact]
    public async Task GetCompleteness_WhenEpisodeCountIsZero_ReturnsIsCompleteTrue()
    {
        var media = await SeedTvShowAsync(507);
        await SeedSeasonAsync(media.Id, 1, "Season 1", 0);

        var result = await CreateHandler().Handle(
            new GetMediaCompletenessQuery(media.Id), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value[0].IsComplete.Should().BeTrue();
        result.Value[0].MissingEpisodeNumbers.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCompleteness_WithoutEpisodeFileLinks_FallsBackToFilenameParsing()
    {
        var media = await SeedTvShowAsync(508);
        var season1 = await SeedSeasonAsync(media.Id, 1, "Season 1", 8);
        var season2 = await SeedSeasonAsync(media.Id, 2, "Season 2", 8);

        for (var ep = 1; ep <= 8; ep++)
        {
            await SeedEpisodeAsync(season1.Id, ep);
            await SeedEpisodeAsync(season2.Id, ep);
        }

        for (var ep = 1; ep <= 8; ep++)
        {
            _context.MediaFiles.Add(new MediaFile
            {
                MediaId = media.Id,
                FilePath = $"/nas/tv/Peacemaker/Season 01/Peacemaker.S01E{ep:D2}.mkv",
                Fingerprint = $"fp_peacemaker_s01e{ep:D2}"
            });
        }

        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateHandler().Handle(
            new GetMediaCompletenessQuery(media.Id), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);

        var s1 = result.Value.Single(s => s.SeasonNumber == 1);
        s1.OwnedCount.Should().Be(8);
        s1.IsComplete.Should().BeTrue();
        s1.MissingEpisodeNumbers.Should().BeEmpty();

        var s2 = result.Value.Single(s => s.SeasonNumber == 2);
        s2.OwnedCount.Should().Be(0);
        s2.IsComplete.Should().BeFalse();
        s2.MissingEpisodeNumbers.Should().BeEquivalentTo([1, 2, 3, 4, 5, 6, 7, 8]);
    }
}

