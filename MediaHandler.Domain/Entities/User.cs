using MediaHandler.Domain.Common;
using MediaHandler.Domain.Enums;

namespace MediaHandler.Domain.Entities;

public class User : BaseEntity
{
    public required string OktaId { get; set; }
    public required string Email { get; set; }
    public string? DisplayName { get; set; }
    public string PreferredLanguage { get; set; } = "en";
    public UserRole Role { get; set; } = UserRole.User;
    public bool IsActive { get; set; } = true;
    public string? ProfilePicturePath { get; set; }

    public ICollection<UserMedia> UserMedias { get; set; } = new List<UserMedia>();
    public ICollection<WishlistItem> WishlistItems { get; set; } = new List<WishlistItem>();
    public ICollection<UserEpisode> UserEpisodes { get; set; } = new List<UserEpisode>();
}