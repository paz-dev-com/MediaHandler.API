using FluentAssertions;
using MediaHandler.Application.Common.Models.Kodi;

namespace MediaHandler.Tests.Kodi;

public class KodiPathTranslatorTests
{
    private static KodiPathMappingSnapshot Mapping(string kodiPrefix, string nasPrefix)
    {
        // Production mappings are normalized at write time — mirror that here.
        return new KodiPathMappingSnapshot(
            KodiPathTranslator.NormalizePrefix(kodiPrefix),
            KodiPathTranslator.NormalizePrefix(nasPrefix));
    }

    [Fact]
    public void Translate_MatchingPrefix_ReturnsRewrittenNasPath()
    {
        var mappings = new[] { Mapping("smb://FREEBOX/Films/", "/nas/Movies/") };

        var result = KodiPathTranslator.Translate(
            "smb://FREEBOX/Films/The Matrix (1999)/The Matrix (1999).mkv", mappings);

        result.Kind.Should().Be(PathTranslationKind.Translated);
        result.TranslatedPath.Should().Be("/nas/Movies/The Matrix (1999)/The Matrix (1999).mkv");
    }

    [Theory]
    // percent-encoding
    [InlineData("smb://FREEBOX/Films/The%20Matrix%20(1999)/The%20Matrix%20(1999).mkv",
        "/nas/Movies/The Matrix (1999)/The Matrix (1999).mkv")]
    // mixed separators
    [InlineData("smb://FREEBOX/Films\\The Matrix (1999)\\The Matrix (1999).mkv",
        "/nas/Movies/The Matrix (1999)/The Matrix (1999).mkv")]
    // letter-case differences relative to the mapping prefix
    [InlineData("smb://freebox/films/The Matrix (1999)/The Matrix (1999).mkv",
        "/nas/Movies/The Matrix (1999)/The Matrix (1999).mkv")]
    public void Translate_PercentEncodedMixedSeparatorsMixedCase_Normalizes(string kodiUri, string expected)
    {
        var mappings = new[] { Mapping("smb://FREEBOX/Films/", "/nas/Movies/") };

        var result = KodiPathTranslator.Translate(kodiUri, mappings);

        result.Kind.Should().Be(PathTranslationKind.Translated);
        result.TranslatedPath.Should().Be(expected);
    }

    [Theory]
    [InlineData("pvr://recordings/tv/show.ts")]
    [InlineData("http://streaming.example.com/movie.mkv")]
    [InlineData("upnp://192.168.1.10/media/file.mkv")]
    [InlineData("plugin://plugin.video.example/play/123")]
    public void Translate_UnsupportedScheme_ReturnsUnsupported(string kodiUri)
    {
        var mappings = new[] { Mapping("smb://FREEBOX/Films/", "/nas/Movies/") };

        var result = KodiPathTranslator.Translate(kodiUri, mappings);

        result.Kind.Should().Be(PathTranslationKind.UnsupportedScheme);
        result.TranslatedPath.Should().BeNull();
    }

    [Fact]
    public void Translate_NoMatchingMapping_ReturnsNoMappingWithDirectoryPrefix()
    {
        var mappings = new[] { Mapping("smb://FREEBOX/Films/", "/nas/Movies/") };

        var result = KodiPathTranslator.Translate(
            "smb://FREEBOX/Series/Breaking Bad/Breaking Bad S01E01.mkv", mappings);

        result.Kind.Should().Be(PathTranslationKind.NoMapping);
        result.TranslatedPath.Should().BeNull();
        result.KodiDirectoryPrefix.Should().Be("smb://FREEBOX/Series/Breaking Bad");
    }

    [Fact]
    public void Translate_OverlappingMappings_FirstInOrderWins()
    {
        var mappings = new[]
        {
            Mapping("smb://FREEBOX/Films/4K/", "/nas/Movies4K/"),
            Mapping("smb://FREEBOX/Films/", "/nas/Movies/")
        };

        var result = KodiPathTranslator.Translate("smb://FREEBOX/Films/4K/Avatar/Avatar.mkv", mappings);

        result.TranslatedPath.Should().Be("/nas/Movies4K/Avatar/Avatar.mkv");
    }

    [Fact]
    public void Translate_OverridePrecedingPersistedMapping_OverrideWins()
    {
        // The start handler prepends normalized overrides, so they win on ties.
        var mappings = new[]
        {
            Mapping("smb://FREEBOX/Films/", "/nas/OverrideTarget/"),
            Mapping("smb://FREEBOX/Films/", "/nas/Movies/")
        };

        var result = KodiPathTranslator.Translate("smb://FREEBOX/Films/x/x.mkv", mappings);

        result.TranslatedPath.Should().Be("/nas/OverrideTarget/x/x.mkv");
    }

    [Theory]
    [InlineData("smb://FREEBOX/Films//The Matrix (1999)/", "/nas/Movies/The Matrix (1999)")]
    [InlineData("smb://FREEBOX/Films/The Matrix (1999)/", "/nas/Movies/The Matrix (1999)")]
    public void Translate_TrailingSlashAndDuplicateSlashes_Collapsed(string kodiUri, string expected)
    {
        var mappings = new[] { Mapping("smb://FREEBOX/Films/", "/nas/Movies/") };

        var result = KodiPathTranslator.Translate(kodiUri, mappings);

        result.Kind.Should().Be(PathTranslationKind.Translated);
        result.TranslatedPath.Should().Be(expected);
    }

    [Fact]
    public void Translate_SchemelessAbsolutePath_AttemptsMapping()
    {
        var mappings = new[] { Mapping("/mnt/kodi/Movies/", "/nas/Movies/") };

        var result = KodiPathTranslator.Translate("/mnt/kodi/Movies/Avatar/Avatar.mkv", mappings);

        result.Kind.Should().Be(PathTranslationKind.Translated);
        result.TranslatedPath.Should().Be("/nas/Movies/Avatar/Avatar.mkv");
    }

    [Fact]
    public void Translate_PrefixBoundary_Respected()
    {
        var mappings = new[] { Mapping("smb://FREEBOX/Films/", "/nas/Movies/") };

        // "Films2" must not be matched by the "Films" prefix
        var result = KodiPathTranslator.Translate("smb://FREEBOX/Films2/x.mkv", mappings);

        result.Kind.Should().Be(PathTranslationKind.NoMapping);
    }
}
