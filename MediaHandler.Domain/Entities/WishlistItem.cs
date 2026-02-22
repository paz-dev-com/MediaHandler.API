using MediaHandler.Domain.Common;

namespace MediaHandler.Domain.Entities;

public class WishlistItem : BaseEntity
{
    public required Guid UserId { get; set; }
    public required int TmdbId { get; set; }
    public required string Title { get; set; }
    public string? PosterPath { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public bool IsAcquired { get; set; }
    public DateTime? AcquiredAt { get; set; }
    public string? Notes { get; set; }

    public User User { get; set; } = null!;
}
