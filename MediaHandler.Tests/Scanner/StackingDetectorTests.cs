#nullable enable
// StackingDetectorTests — Kodi multi-part stacking detection
// SOURCE: https://kodi.wiki/view/Advancedsettings.xml#stackingregex
// SOURCE: Kodi wiki "Stacked movies" — cd1/cd2, part1/part2, disc1/disc2, (a)/(b), pt1/pt2

using FluentAssertions;
using MediaHandler.Application.Common.Models.Scanner;
using MediaHandler.Infrastructure.Nas.Scanner;

namespace MediaHandler.Tests.Scanner;

/// <summary>
/// Tests for <see cref="StackingDetector"/> verifying all stacking suffix families.
/// Each row derives from publicly documented Kodi stacking conventions.
/// No text is copied from /home/tpfeifer/Repos/xbmc-master/ (R-001).
/// </summary>
public class StackingDetectorTests
{
    private readonly StackingDetector _sut = new();

    // =========================================================================
    // cd1/cd2 family
    // SOURCE: Kodi wiki advancedsettings stackregex — cd1/cd2 is the primary suffix
    // =========================================================================

    [Fact]
    public void Group_Cd1Cd2WithSameBase_ReturnsSingleGroup()
    {
        var files = new[]
        {
            MakeFile("/nas/Movies/Kill Bill (2003)/Kill.Bill.2003.cd1.mkv"),
            MakeFile("/nas/Movies/Kill Bill (2003)/Kill.Bill.2003.cd2.mkv"),
        };

        var groups = _sut.Group(files);

        groups.Should().HaveCount(1);
        groups[0].Parts.Should().HaveCount(2);
        groups[0].Discriminator.Should().Be("cd");
        groups[0].BaseTitle.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Group_CdWithUppercase_RecognisesStack()
    {
        // SOURCE: Observed Kodi behaviour — case-insensitive suffix matching
        var files = new[]
        {
            MakeFile("/nas/Movies/Film/Film.CD1.mkv"),
            MakeFile("/nas/Movies/Film/Film.CD2.mkv"),
        };
        var groups = _sut.Group(files);
        groups.Should().HaveCount(1);
        groups[0].Discriminator.ToLower().Should().Be("cd");
    }

    // =========================================================================
    // part1/part2 family
    // SOURCE: Kodi wiki — "part1, part2 may be used as an alternative"
    // =========================================================================

    [Fact]
    public void Group_Part1Part2_ReturnsSingleGroup()
    {
        var files = new[]
        {
            MakeFile("/nas/Movies/Gettysburg (1993)/Gettysburg.part1.mkv"),
            MakeFile("/nas/Movies/Gettysburg (1993)/Gettysburg.part2.mkv"),
        };
        var groups = _sut.Group(files);
        groups.Should().HaveCount(1);
        groups[0].Discriminator.Should().Be("part");
    }

    [Fact]
    public void Group_PartWithDash_ReturnsSingleGroup()
    {
        // SOURCE: Observed Kodi behaviour — "-part1" dash-separated variant
        var files = new[]
        {
            MakeFile("/nas/Movies/Movie/Movie-part1.mkv"),
            MakeFile("/nas/Movies/Movie/Movie-part2.mkv"),
        };
        var groups = _sut.Group(files);
        groups.Should().HaveCount(1);
    }

    // =========================================================================
    // disc1/disc2 family
    // SOURCE: Kodi wiki — "disc1/disc2 is a valid stacking keyword"
    // =========================================================================

    [Fact]
    public void Group_Disc1Disc2_ReturnsSingleGroup()
    {
        var files = new[]
        {
            MakeFile("/nas/Movies/Lawrence of Arabia (1962)/Lawrence.of.Arabia.disc1.mkv"),
            MakeFile("/nas/Movies/Lawrence of Arabia (1962)/Lawrence.of.Arabia.disc2.mkv"),
        };
        var groups = _sut.Group(files);
        groups.Should().HaveCount(1);
        groups[0].Discriminator.Should().Be("disc");
    }

    // =========================================================================
    // (a)/(b) parenthesised letter family
    // SOURCE: Kodi wiki advancedsettings stackregex — "(a)" and "(b)" bracketed letters
    // =========================================================================

    [Fact]
    public void Group_ParenLetterAB_ReturnsSingleGroup()
    {
        var files = new[]
        {
            MakeFile("/nas/Movies/Movie (2001)/Movie (2001) (a).mkv"),
            MakeFile("/nas/Movies/Movie (2001)/Movie (2001) (b).mkv"),
        };
        var groups = _sut.Group(files);
        groups.Should().HaveCount(1);
    }

    // =========================================================================
    // pt1/pt2 abbreviated family
    // SOURCE: Kodi wiki — "pt1, pt2 are recognised abbreviations for part"
    // =========================================================================

    [Fact]
    public void Group_Pt1Pt2_ReturnsSingleGroup()
    {
        var files = new[]
        {
            MakeFile("/nas/Movies/Film/Film.pt1.mkv"),
            MakeFile("/nas/Movies/Film/Film.pt2.mkv"),
        };
        var groups = _sut.Group(files);
        groups.Should().HaveCount(1);
        groups[0].Discriminator.Should().Be("pt");
    }

    // =========================================================================
    // Three-part stacks
    // SOURCE: Observed Kodi behaviour — stacking supports cd1/cd2/cd3
    // =========================================================================

    [Fact]
    public void Group_ThreeParts_ReturnsSingleGroupWithThreeParts()
    {
        var files = new[]
        {
            MakeFile("/nas/Movies/Long Film/LongFilm.cd1.mkv"),
            MakeFile("/nas/Movies/Long Film/LongFilm.cd2.mkv"),
            MakeFile("/nas/Movies/Long Film/LongFilm.cd3.mkv"),
        };
        var groups = _sut.Group(files);
        groups.Should().HaveCount(1);
        groups[0].Parts.Should().HaveCount(3);
    }

    // =========================================================================
    // Non-stacking cases
    // =========================================================================

    [Fact]
    public void Group_NoStackingSuffix_ReturnsEmpty()
    {
        // SOURCE: Kodi wiki — only files with recognised stack suffixes are grouped
        var files = new[]
        {
            MakeFile("/nas/Movies/The Matrix (1999)/The.Matrix.mkv"),
            MakeFile("/nas/Movies/Inception (2010)/Inception.mkv"),
        };
        var groups = _sut.Group(files);
        groups.Should().BeEmpty();
    }

    [Fact]
    public void Group_SingleFileLooksLikeStack_ReturnsEmpty()
    {
        // A single "cd1" file without a matching cd2 is not a stack
        var files = new[]
        {
            MakeFile("/nas/Movies/Movie/Movie.cd1.mkv"),
        };
        var groups = _sut.Group(files);
        groups.Should().BeEmpty();
    }

    [Fact]
    public void Group_DifferentBaseNames_StackedSeparately()
    {
        // Two different movies in the same folder, each stacked separately
        var files = new[]
        {
            MakeFile("/nas/Movies/Kill.Bill.cd1.mkv"),
            MakeFile("/nas/Movies/Kill.Bill.cd2.mkv"),
            MakeFile("/nas/Movies/Goodfellas.cd1.mkv"),
            MakeFile("/nas/Movies/Goodfellas.cd2.mkv"),
        };
        var groups = _sut.Group(files);
        groups.Should().HaveCount(2);
    }

    [Fact]
    public void Group_Parts_AreOrderedByStackOrdinal()
    {
        // SOURCE: Kodi wiki — parts must appear in order for correct playback
        var files = new[]
        {
            MakeFile("/nas/Movies/Movie/Movie.cd2.mkv"),
            MakeFile("/nas/Movies/Movie/Movie.cd1.mkv"),
        };
        var groups = _sut.Group(files);
        groups.Should().HaveCount(1);
        groups[0].Parts[0].FileName.Should().Contain("cd1");
        groups[0].Parts[1].FileName.Should().Contain("cd2");
    }

    private static NasFileEntry MakeFile(string path)
    {
        var name = System.IO.Path.GetFileName(path);
        return new NasFileEntry(path, name, 1_073_741_824L, DateTime.UtcNow, false, "mkv");
    }
}

