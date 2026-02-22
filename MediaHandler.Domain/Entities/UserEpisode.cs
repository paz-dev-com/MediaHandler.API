using MediaHandler.Domain.Common;

namespace MediaHandler.Domain.Entities;

public class UserEpisode : BaseEntity
{
    public required Guid UserId { get; set; }
    public required Guid EpisodeId { get; set; }
    public bool IsWatched { get; set; }
    public DateTime? WatchedAt { get; set; }

    public User User { get; set; } = null!;
    public TvEpisode Episode { get; set; } = null!;
}
