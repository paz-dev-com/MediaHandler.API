using AutoMapper;
using MediaHandler.Application.Features.Wishlist.DTOs;
using MediaHandler.Domain.Entities;

namespace MediaHandler.Application.Common.Mappings;

public class WishlistMappingProfile : Profile
{
    public WishlistMappingProfile()
    {
        CreateMap<WishlistItem, WishlistItemDto>();
    }
}
