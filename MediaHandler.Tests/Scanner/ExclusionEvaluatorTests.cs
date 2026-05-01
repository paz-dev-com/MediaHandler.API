// All rules derived clean-room from documented Kodi behaviour (R-001).
// SOURCE: https://kodi.wiki/view/Advancedsettings.xml (exclusion settings)
// SOURCE: https://kodi.wiki/view/Naming_video_files (sample, extras conventions)

using FluentAssertions;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models.Scanner;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Enums;
using MediaHandler.Infrastructure.Nas.Scanner;

namespace MediaHandler.Tests.Scanner;

/// <summary>
///     Tests for <see cref="ExclusionEvaluator" /> covering every built-in exclusion rule.
/// </summary>
public class ExclusionEvaluatorTests
{
    private static readonly LibraryRoot _moviesRoot = new()
    {
        Path = "/nas/Movies",
        Kind = LibraryRootKind.Movies,
        IsEnabled = true
    };

    private readonly IExclusionEvaluator _sut = new ExclusionEvaluator();

    private static ExclusionContext DefaultCtx(LibraryRoot? root = null)
    {
        return new ExclusionContext(root ?? _moviesRoot, KodiRegexCatalog.DefaultExclusionRules);
    }

    // =========================================================================
    // Video extension allow-list
    // SOURCE: Kodi wiki advancedsettings <videoextensions> default list
    // =========================================================================

    [Theory]
    [InlineData("movie.mkv")]
    [InlineData("movie.mp4")]
    [InlineData("movie.avi")]
    [InlineData("movie.m4v")]
    [InlineData("movie.mov")]
    [InlineData("movie.wmv")]
    [InlineData("movie.flv")]
    [InlineData("movie.ts")]
    [InlineData("movie.m2ts")]
    [InlineData("movie.mpg")]
    [InlineData("movie.mpeg")]
    [InlineData("movie.3gp")]
    [InlineData("movie.ogv")]
    [InlineData("movie.webm")]
    [InlineData("movie.divx")]
    [InlineData("movie.iso")]
    [InlineData("movie.vob")]
    public void Evaluate_VideoExtension_NotExcluded(string filename)
    {
        var entry = MakeFile($"/nas/Movies/{filename}");
        var verdict = _sut.Evaluate(entry, DefaultCtx());
        verdict.IsExcluded.Should().BeFalse($"{filename} is a valid video extension");
    }

    [Theory]
    [InlineData("poster.jpg")] // SOURCE: Kodi wiki — image files not scanned
    [InlineData("cover.png")]
    [InlineData("nfo.xml")]
    [InlineData("movie.nfo")]
    [InlineData("subtitle.srt")]
    [InlineData("subtitle.sub")]
    [InlineData("info.txt")]
    [InlineData("readme.md")]
    [InlineData("db.sqlite")]
    [InlineData("meta.json")]
    public void Evaluate_NonVideoExtension_IsExcluded(string filename)
    {
        var entry = MakeFile($"/nas/Movies/{filename}");
        var verdict = _sut.Evaluate(entry, DefaultCtx());
        verdict.IsExcluded.Should().BeTrue($"{filename} is not a video");
        verdict.RuleId.Should().NotBeNullOrEmpty();
    }

    // =========================================================================
    // Sample / trailer patterns in filename
    // SOURCE: Kodi wiki advancedsettings <trailerextensions> and sample naming
    // SOURCE: Observed Kodi behaviour — sample suffix triggers exclusion
    // =========================================================================

    [Theory]
    [InlineData("/nas/Movies/Movie (2020)/Movie (2020)-sample.mkv", "sample-filename")]
    [InlineData("/nas/Movies/Movie (2020)/Movie.Sample.mkv", "sample-filename")]
    [InlineData("/nas/Movies/sample.mkv", "sample-filename")]
    [InlineData("/nas/Movies/Movie (2020)/Movie (2020)-trailer.mkv", "trailer-filename")]
    [InlineData("/nas/Movies/Movie (2020)/movie-trailer.mkv", "trailer-filename")]
    public void Evaluate_SampleOrTrailerFilename_IsExcluded(string path, string expectedRuleId)
    {
        var entry = MakeFile(path);
        var verdict = _sut.Evaluate(entry, DefaultCtx());
        verdict.IsExcluded.Should().BeTrue($"'{path}' matches a sample/trailer pattern");
        verdict.RuleId.Should().Be(expectedRuleId);
    }

    // =========================================================================
    // Exclusion subfolder names
    // SOURCE: Kodi wiki — "Extras, Featurettes, Trailers, Sample folders are excluded"
    // =========================================================================

    [Theory]
    [InlineData("/nas/Movies/The Matrix (1999)/Sample/Matrix-sample.mkv", "sample-folder")]
    [InlineData("/nas/Movies/The Matrix (1999)/Extras/deleted-scenes.mkv", "extras-folder")]
    [InlineData("/nas/Movies/The Matrix (1999)/Featurettes/making-of.mkv", "featurettes-folder")]
    [InlineData("/nas/Movies/The Matrix (1999)/Trailers/trailer.mkv", "trailers-folder")]
    [InlineData("/nas/Movies/The Matrix (1999)/Behind the Scenes/film.mkv", "behind-the-scenes-folder")]
    [InlineData("/nas/Movies/The Matrix (1999)/Shorts/short.mkv", "shorts-folder")]
    [InlineData("/nas/Movies/The Matrix (1999)/Scenes/clip.mkv", "scenes-folder")]
    [InlineData("/nas/Movies/The Matrix (1999)/Interviews/interview.mkv", "interviews-folder")]
    [InlineData("/nas/Movies/The Matrix (1999)/deleted scenes/clip.mkv", "deleted-scenes-folder")]
    public void Evaluate_ExcludedSubfolder_IsExcluded(string path, string expectedRuleId)
    {
        var entry = MakeFile(path);
        var verdict = _sut.Evaluate(entry, DefaultCtx());
        verdict.IsExcluded.Should().BeTrue($"'{path}' is in an excluded subfolder");
        verdict.RuleId.Should().Be(expectedRuleId);
    }

    // =========================================================================
    // Hidden folders (Unix dot-prefix)
    // SOURCE: Observed Kodi behaviour — directories beginning with '.' are skipped
    // =========================================================================

    [Theory]
    [InlineData("/nas/Movies/.recycle/oldfile.mkv", "hidden-folder")]
    [InlineData("/nas/Movies/.hidden/movie.mkv", "hidden-folder")]
    [InlineData("/nas/Movies/Movie (2020)/.actors/actor.mkv", "hidden-folder")]
    [InlineData("/nas/Movies/.DS_Store/file.mkv", "hidden-folder")]
    public void Evaluate_HiddenFolder_IsExcluded(string path, string expectedRuleId)
    {
        var entry = MakeFile(path);
        var verdict = _sut.Evaluate(entry, DefaultCtx());
        verdict.IsExcluded.Should().BeTrue($"hidden folder in '{path}' should trigger exclusion");
        verdict.RuleId.Should().Be(expectedRuleId);
    }

    // =========================================================================
    // .nomedia marker file (subtree skip)
    // SOURCE: Kodi advancedsettings — presence of .nomedia in a folder excludes it
    // =========================================================================

    [Fact]
    public void Evaluate_NomediaFile_IsExcluded()
    {
        // The .nomedia file itself should be detected as a marker; the evaluator
        // must return an exclusion so the pipeline can mark the whole folder tree.
        var entry = MakeFile("/nas/Movies/Movie (2020)/.nomedia");
        var verdict = _sut.Evaluate(entry, DefaultCtx());
        verdict.IsExcluded.Should().BeTrue(".nomedia is a marker file that suppresses scanning");
        verdict.RuleId.Should().Be("nomedia-marker");
    }

    [Fact]
    public void Evaluate_FileInNomediaFolder_ExcludedByMarker()
    {
        // Any file under a folder that contains a .nomedia marker should be excluded.
        // The evaluator context carries a set of folders marked as nomedia.
        var rules = KodiRegexCatalog.DefaultExclusionRules;
        var nomediaCtx = new ExclusionContext(
            _moviesRoot,
            rules,
            ["/nas/Movies/Protected Folder"]);

        var entry = MakeFile("/nas/Movies/Protected Folder/movie.mkv");
        var verdict = _sut.Evaluate(entry, nomediaCtx);
        verdict.IsExcluded.Should().BeTrue("file is inside a .nomedia-protected folder");
        verdict.RuleId.Should().Be("nomedia-subtree");
    }

    // =========================================================================
    // Directories themselves
    // =========================================================================

    [Fact]
    public void Evaluate_Directory_IsExcludedFromMediaProcessing()
    {
        var dir = new NasFileEntry("/nas/Movies/The Matrix (1999)", "The Matrix (1999)", 0, DateTime.UtcNow, true,
            null);
        var verdict = _sut.Evaluate(dir, DefaultCtx());
        verdict.IsExcluded.Should().BeTrue("directories are not media files");
        verdict.RuleId.Should().Be("not-a-file");
    }

    // =========================================================================
    // Negative cases — must NOT be excluded
    // =========================================================================

    [Theory]
    [InlineData("/nas/Movies/The Matrix (1999)/The Matrix (1999).mkv")]
    [InlineData("/nas/Movies/Inception (2010)/Inception (2010).mkv")]
    [InlineData("/nas/Movies/Flat.Movie.2020.mkv")]
    public void Evaluate_ValidMovieFile_NotExcluded(string path)
    {
        var entry = MakeFile(path);
        var verdict = _sut.Evaluate(entry, DefaultCtx());
        verdict.IsExcluded.Should().BeFalse($"'{path}' is a valid movie file");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static NasFileEntry MakeFile(string path)
    {
        var name = Path.GetFileName(path);
        var ext = Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
        return new NasFileEntry(path, name, 1_073_741_824L, DateTime.UtcNow, false, ext.Length == 0 ? null : ext);
    }
}