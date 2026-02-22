using MediaHandler.Domain.Common;

namespace MediaHandler.Domain.Entities;

public class TvEpisode : BaseEntity
{
    public required Guid SeasonId { get; set; }
    public required int EpisodeNumber { get; set; }
    public required string Name { get; set; }
    public string? Overview { get; set; }
    public DateTime? AirDate { get; set; }
    public string? StillPath { get; set; }
    public int? Runtime { get; set; }

    public TvSeason Season { get; set; } = null!;
    public ICollection<UserEpisode> UserEpisodes { get; set; } = new List<UserEpisode>();
}
