using FluentAssertions;
using MediaHandler.Application.Common.Models.Kodi;

namespace MediaHandler.Tests.Kodi;

public class KodiDbFileNameTests
{
    [Theory]
    [InlineData("MyVideos121.db", 121)]
    [InlineData("MyVideos119.db", 119)]
    [InlineData("MyVideos131.db", 131)]
    [InlineData("myvideos121.db", 121)] // case-insensitive
    [InlineData("MYVIDEOS131.DB", 131)]
    public void TryParseVersion_ValidName_ReturnsVersion(string fileName, int expected)
    {
        var result = KodiDbFileName.TryParseVersion(fileName, out var version);

        result.Should().BeTrue();
        version.Should().Be(expected);
    }

    [Theory]
    [InlineData("MyVideos.db")] // no version suffix
    [InlineData("videos.db")]
    [InlineData("MyVideos121.db.backup")]
    [InlineData("MyVideosABC.db")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParseVersion_NoVersionSuffix_ReturnsFalse(string? fileName)
    {
        var result = KodiDbFileName.TryParseVersion(fileName, out _);

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("MyMusic82.db")]
    [InlineData("MyMusic.db")]
    public void TryParseVersion_MusicDbName_ReturnsFalse(string fileName)
    {
        var result = KodiDbFileName.TryParseVersion(fileName, out _);

        result.Should().BeFalse();
    }
}
