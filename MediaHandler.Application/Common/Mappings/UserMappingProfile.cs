using AutoMapper;
using MediaHandler.Application.Features.Auth.DTOs;
using MediaHandler.Domain.Entities;

namespace MediaHandler.Application.Common.Mappings;

public class UserMappingProfile : Profile
{
    public UserMappingProfile()
    {
        CreateMap<User, UserDto>()
            .ForMember(dest => dest.Role, opt => opt.MapFrom(src => src.Role.ToString()));
    }
}