namespace MediaHandler.Application.Common.Models.Scanner;

/// <summary>
///     Result of <c>INfoParser.ParseAsync</c>.
///     When <see cref="ParsedSuccessfully" /> is <c>false</c>, all optional fields are null
///     and <see cref="Warning" /> explains the failure.
/// </summary>
public record NfoParseResult(
    bool ParsedSuccessfully,
    string? Title,
    int? Year,
    int? TmdbId,
    string? ImdbId,
    int? Season,
    int? Episode,
    string? Warning = null)
{
    /// <summary>Convenience factory: a malformed / unreadable NFO.</summary>
    public static NfoParseResult Malformed(string warning)
    {
        return new NfoParseResult(false, null, null, null, null, null, null, warning);
    }
}