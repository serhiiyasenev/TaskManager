using AutoMapper;
using BLL.Models.Users;
using DAL.Entities;

namespace BLL.Mapping;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<User, FullUserDto>();
    }
}