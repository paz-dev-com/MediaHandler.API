using MediaHandler.Domain.Enums;

namespace MediaHandler.Application.Features.Wishlist.DTOs;

public record WishlistItemDto(
    Guid Id,
    int TmdbId,
    string Title,
    string? PosterPath,
    DateTime? ReleaseDate,
    bool IsAcquired,
    DateTime? AcquiredAt,
    string? Notes,
    DateTime CreatedAt);
