using AutoMapper;
using DAL.Entities;
using BLL.Models.Projects;
using BLL.Models.Tasks;
using BLL.Models.Users;
using BLL.Models.Teams;

namespace BLL.Mapping;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        // User mappings
        CreateMap<User, UserDto>();
        CreateMap<RegisterUserDto, User>()
            .ForMember(dest => dest.RegisteredAt, opt => opt.MapFrom(_ => DateTime.UtcNow))
            .ForMember(dest => dest.Id, opt => opt.Ignore());

        CreateMap<UpdateUserDto, User>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.RegisteredAt, opt => opt.Ignore());

        // Project mappings
        CreateMap<Project, ProjectDto>();
        CreateMap<Project, FullProjectDto>();

        // Task mappings
        CreateMap<DAL.Entities.Task, TaskDto>()
            .ForMember(dest => dest.State, opt => opt.MapFrom(src => src.State.ToString()));
        
        CreateMap<DAL.Entities.Task, TaskWithPerformerDto>();

        // Team mappings
        CreateMap<Team, TeamDto>();
        CreateMap<Team, TeamWithMembersDto>()
            .ForMember(dest => dest.Members, opt => opt.MapFrom(src => src.Users));
        
        CreateMap<CreateTeamDto, Team>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.MapFrom(_ => DateTime.UtcNow))
            .ForMember(dest => dest.Users, opt => opt.Ignore())
            .ForMember(dest => dest.Projects, opt => opt.Ignore());

        CreateMap<UpdateTeamDto, Team>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.Users, opt => opt.Ignore())
            .ForMember(dest => dest.Projects, opt => opt.Ignore());
    }
}
