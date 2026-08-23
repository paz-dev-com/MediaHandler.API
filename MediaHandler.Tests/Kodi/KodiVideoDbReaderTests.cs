using FluentAssertions;
using MediaHandler.Application.Common.Models.Kodi;
using MediaHandler.Infrastructure.Kodi;
using MediaHandler.Infrastructure.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MediaHandler.Tests.Kodi;

public class KodiVideoDbReaderTests : IDisposable
{
    private readonly List<string> _fixturePaths = [];

    private KodiVideoDbReader CreateReader()
    {
        return new KodiVideoDbReader(
            Options.Create(new KodiImportOptions()),
            NullLogger<KodiVideoDbReader>.Instance);
    }

    private string Track(string path)
    {
        _fixturePaths.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var path in _fixturePaths)
            KodiTestDbBuilder.Delete(path);
    }

    // =========================================================================
    // ValidateAsync
    // =========================================================================

    [Fact]
    public async Task ValidateAsync_ValidV121Structure_ReturnsValid()
    {
        var path = Track(KodiTestDbBuilder.CreateVideoDb(121));

        var result = await CreateReader().ValidateAsync(path, 121, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
    }

    [Fact]
    public async Task ValidateAsync_ValidV119Structure_ReturnsValid()
    {
        var path = Track(KodiTestDbBuilder.CreateVideoDb(119));

        var result = await CreateReader().ValidateAsync(path, 119, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_MissingVideoTables_ReturnsNotKodiVideoDb()
    {
        var path = Track(KodiTestDbBuilder.CreateSqliteWithoutVideoTables());

        var result = await CreateReader().ValidateAsync(path, 121, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_KODI_DB");
        result.ErrorMessage.Should().Contain("not a Kodi video database");
    }

    [Fact]
    public async Task ValidateAsync_CorruptFile_ReturnsInvalidWithGuidance()
    {
        var path = Track(KodiTestDbBuilder.CreateGarbageFile());

        var result = await CreateReader().ValidateAsync(path, 121, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_KODI_DB");
        result.ErrorMessage.Should().Contain("not a SQLite database");
    }

    [Fact]
    public async Task ValidateAsync_UnsupportedVersion_ReturnsErrorNamingVersion()
    {
        var path = Track(KodiTestDbBuilder.CreateVideoDb(121));

        var result = await CreateReader().ValidateAsync(path, 999, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("UNSUPPORTED_VERSION");
        result.ErrorMessage.Should().Contain("999");
        result.ErrorMessage.Should().Contain("119");
    }

    // =========================================================================
    // ReadAsync
    // =========================================================================

    [Fact]
    public async Task ReadAsync_EmptyLibrary_ReturnsEmptySnapshot()
    {
        var path = Track(KodiTestDbBuilder.CreateVideoDb(121));

        var snapshot = await CreateReader().ReadAsync(path, 121, TestContext.Current.CancellationToken);

        snapshot.Movies.Should().BeEmpty();
        snapshot.Shows.Should().BeEmpty();
        snapshot.MusicVideos.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAsync_MoviesWithUniqueIds_ReturnsTitlesYearsExternalIds()
    {
        var path = Track(KodiTestDbBuilder.CreateVideoDb(121,
            movies:
            [
                new TestKodiMovie(1, "The Matrix", "Matrix", 1999,
                    "smb://FREEBOX/Films/The Matrix (1999)/", "The Matrix (1999).mkv"),
                new TestKodiMovie(2, "Amélie", null, 2001)
            ],
            uniqueIds:
            [
                new TestKodiUniqueId(1, "movie", "tmdb", "603"),
                new TestKodiUniqueId(1, "movie", "imdb", "tt0133093"),
                new TestKodiUniqueId(2, "movie", "imdb", "tt0211915")
            ]));

        var snapshot = await CreateReader().ReadAsync(path, 121, TestContext.Current.CancellationToken);

        snapshot.Movies.Should().HaveCount(2);

        var matrix = snapshot.Movies.Single(m => m.KodiMovieId == 1);
        matrix.Title.Should().Be("The Matrix");
        matrix.OriginalTitle.Should().Be("Matrix");
        matrix.Year.Should().Be(1999);
        matrix.ExternalIds.Should().BeEquivalentTo(new[]
        {
            new KodiExternalId("tmdb", "603"),
            new KodiExternalId("imdb", "tt0133093")
        });
        matrix.FileRefs.Should().ContainSingle()
            .Which.Should().Be("smb://FREEBOX/Films/The Matrix (1999)/The Matrix (1999).mkv");

        var amelie = snapshot.Movies.Single(m => m.KodiMovieId == 2);
        amelie.OriginalTitle.Should().BeNull();
        amelie.ExternalIds.Should().ContainSingle()
            .Which.Should().Be(new KodiExternalId("imdb", "tt0211915"));
    }

    [Fact]
    public async Task ReadAsync_V119MovieYear_ReadFromC07Column()
    {
        var path = Track(KodiTestDbBuilder.CreateVideoDb(119,
            movies: [new TestKodiMovie(1, "The Matrix", Year: 1999)]));

        var snapshot = await CreateReader().ReadAsync(path, 119, TestContext.Current.CancellationToken);

        snapshot.Movies.Should().ContainSingle()
            .Which.Year.Should().Be(1999);
    }

    [Fact]
    public async Task ReadAsync_StackedMovie_ExpandsStackUriIntoOrderedParts()
    {
        const string stackFileName =
            "stack://smb://FREEBOX/Films/Avatar (2009)/Avatar (2009) CD1.mkv , " +
            "smb://FREEBOX/Films/Avatar (2009)/Avatar (2009) CD2.mkv";

        var path = Track(KodiTestDbBuilder.CreateVideoDb(121,
            movies:
            [
                new TestKodiMovie(7, "Avatar", Year: 2009,
                    Directory: "smb://FREEBOX/Films/Avatar (2009)/", FileName: stackFileName)
            ]));

        var snapshot = await CreateReader().ReadAsync(path, 121, TestContext.Current.CancellationToken);

        var movie = snapshot.Movies.Should().ContainSingle().Which;
        movie.FileRefs.Should().Equal(
            "smb://FREEBOX/Films/Avatar (2009)/Avatar (2009) CD1.mkv",
            "smb://FREEBOX/Films/Avatar (2009)/Avatar (2009) CD2.mkv");
    }

    [Fact]
    public async Task ReadAsync_MultiEpisodeFile_ReturnsEpisodesSharingFileRef()
    {
        var path = Track(KodiTestDbBuilder.CreateVideoDb(121,
            shows:
            [
                new TestKodiShow(10, "Breaking Bad", "2008-01-20",
                [
                    new TestKodiEpisode(100, 1, 1, "Pilot",
                        "smb://FREEBOX/Series/Breaking Bad/", "Breaking Bad S01E01-E02.mkv"),
                    new TestKodiEpisode(101, 1, 2, "Cat's in the Bag...",
                        "smb://FREEBOX/Series/Breaking Bad/", "Breaking Bad S01E01-E02.mkv")
                ])
            ]));

        var snapshot = await CreateReader().ReadAsync(path, 121, TestContext.Current.CancellationToken);

        var show = snapshot.Shows.Should().ContainSingle().Which;
        show.Year.Should().Be(2008);
        show.Episodes.Should().HaveCount(2);
        show.Episodes.Select(e => e.FileRef).Distinct().Should().ContainSingle()
            .Which.Should().Be("smb://FREEBOX/Series/Breaking Bad/Breaking Bad S01E01-E02.mkv");
        show.Episodes.Select(e => (e.SeasonNumber, e.EpisodeNumber)).Should().Equal([(1, 1), (1, 2)]);
    }

    [Fact]
    public async Task ReadAsync_SeasonZeroEpisodes_Included()
    {
        var path = Track(KodiTestDbBuilder.CreateVideoDb(121,
            shows:
            [
                new TestKodiShow(10, "Show", "2010-05-05",
                [
                    new TestKodiEpisode(100, 0, 1, "Behind the Scenes",
                        "smb://FREEBOX/Series/Show/", "Show S00E01.mkv"),
                    new TestKodiEpisode(101, 1, 1, "Pilot",
                        "smb://FREEBOX/Series/Show/", "Show S01E01.mkv")
                ])
            ]));

        var snapshot = await CreateReader().ReadAsync(path, 121, TestContext.Current.CancellationToken);

        var show = snapshot.Shows.Should().ContainSingle().Which;
        show.Episodes.Should().HaveCount(2);
        show.Episodes.Should().Contain(e => e.SeasonNumber == 0 && e.EpisodeNumber == 1);
    }

    [Fact]
    public async Task ReadAsync_MusicVideos_ReturnedForCounting()
    {
        var path = Track(KodiTestDbBuilder.CreateVideoDb(121,
            musicVideos: [new TestKodiMusicVideo(1, "Thriller"), new TestKodiMusicVideo(2, "Cliff'ard")]));

        var snapshot = await CreateReader().ReadAsync(path, 121, TestContext.Current.CancellationToken);

        snapshot.MusicVideos.Should().BeEquivalentTo(new[]
        {
            new KodiMusicVideoItem(1, "Thriller"),
            new KodiMusicVideoItem(2, "Cliff'ard")
        });
    }

    [Fact]
    public async Task ReadAsync_NonAsciiTitles_RoundTripUnchanged()
    {
        var path = Track(KodiTestDbBuilder.CreateVideoDb(121,
            movies: [new TestKodiMovie(1, "Les égarés — 流浪地球 (Français)", Year: 2019)]));

        var snapshot = await CreateReader().ReadAsync(path, 121, TestContext.Current.CancellationToken);

        snapshot.Movies.Should().ContainSingle()
            .Which.Title.Should().Be("Les égarés — 流浪地球 (Français)");
    }

    [Fact]
    public async Task ReadAsync_PercentEncodedPaths_ReturnedRaw()
    {
        var path = Track(KodiTestDbBuilder.CreateVideoDb(121,
            movies:
            [
                new TestKodiMovie(1, "The Matrix", Year: 1999,
                    Directory: "smb://FREEBOX/Films/The%20Matrix%20(1999)/",
                    FileName: "The%20Matrix%20(1999).mkv")
            ]));

        var snapshot = await CreateReader().ReadAsync(path, 121, TestContext.Current.CancellationToken);

        // Decoding is the translator's job — the reader returns raw strings.
        snapshot.Movies.Should().ContainSingle()
            .Which.FileRefs.Should().ContainSingle()
            .Which.Should().Be("smb://FREEBOX/Films/The%20Matrix%20(1999)/The%20Matrix%20(1999).mkv");
    }
}
