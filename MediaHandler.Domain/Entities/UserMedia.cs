using MediaHandler.Domain.Common;

namespace MediaHandler.Domain.Entities;

public class UserMedia : BaseEntity
{
    public required Guid UserId { get; set; }
    public required Guid MediaId { get; set; }
    public bool IsWatched { get; set; }
    public DateTime? WatchedAt { get; set; }
    public decimal? PersonalRating { get; set; }
    public string? Notes { get; set; }

    public User User { get; set; } = null!;
    public Media Media { get; set; } = null!;
}