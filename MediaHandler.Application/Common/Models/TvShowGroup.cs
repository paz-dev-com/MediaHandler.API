using System.Security.Cryptography;
using System.Text;

namespace MediaHandler.Application.Common.Models;

/// <summary>
///     Transient (non-persisted) grouping of TV show episodes derived from
///     <see cref="MediaHandler.Domain.Entities.ScanItemDecision" /> rows
///     that share the same <c>ParsedTitle</c> within a given scan run.
///     <para>
///         The <see cref="GroupId" /> is a deterministic UUID-v5-style GUID computed from
///         the scan run ID and the lowercased parsed show name, ensuring the same show in the
///         same scan always produces the same group ID.
///     </para>
/// </summary>
public sealed class TvShowGroup
{
    public required Guid GroupId { get; init; }
    public required string ParsedShowName { get; init; }
    public required List<Guid> DecisionIds { get; init; }

    /// <summary>Number of episode decisions in this group.</summary>
    public int EpisodeCount => DecisionIds.Count;

    /// <summary>
    ///     Computes a deterministic GUID for the given scan run and parsed show name.
    ///     Uses SHA-256 with UUID version-5 variant bits set on the first 16 bytes of the hash.
    /// </summary>
    public static Guid ComputeGroupId(Guid scanId, string parsedShowName)
    {
        var input = $"{scanId}|{parsedShowName.ToLowerInvariant()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));

        // Set UUID version 5 bits
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50); // version 5
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // variant RFC 4122

        return new Guid(hash[..16]);
    }
}

