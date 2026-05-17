using MediaHandler.Domain.Common;

namespace MediaHandler.Domain.Entities;

public class TvSeason : BaseEntity
{
    public required Guid MediaId { get; set; }
    public required int SeasonNumber { get; set; }
    public required string Name { get; set; }
    public string? Overview { get; set; }
    public DateTime? AirDate { get; set; }
    public string? PosterPath { get; set; }
    public int? EpisodeCount { get; set; }

    public Media Media { get; set; } = null!;
    public ICollection<TvEpisode> TvEpisodes { get; set; } = new List<TvEpisode>();
}