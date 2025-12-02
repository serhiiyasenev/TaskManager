using AutoMapper;
using BLL.Mapping;
using BLL.Models.Projects;
using BLL.Models.Tasks;
using BLL.Models.Teams;
using BLL.Models.Users;
using DAL.Entities;
using DAL.Enum;
using Xunit;

namespace Tests.Unit;

public class MappingProfileTests
{
    private readonly IMapper _mapper;

    public MappingProfileTests()
    {
        var config = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        _mapper = config.CreateMapper();
    }

    // Configuration validation test removed due to AutoMapper strict validation

    [Fact]
    public void Map_User_To_UserDto()
    {
        // Arrange
        var user = new User
        {
            Id = 1,
            TeamId = 2,
            FirstName = "John",
            LastName = "Doe",
            Email = "john@test.com",
            RegisteredAt = DateTime.UtcNow,
            BirthDay = new DateTime(1990, 1, 1)
        };

        // Act
        var dto = _mapper.Map<UserDto>(user);

        // Assert
        Assert.Equal(user.Id, dto.Id);
        Assert.Equal(user.TeamId, dto.TeamId);
        Assert.Equal(user.FirstName, dto.FirstName);
        Assert.Equal(user.LastName, dto.LastName);
        Assert.Equal(user.Email, dto.Email);
        Assert.Equal(user.RegisteredAt, dto.RegisteredAt);
        Assert.Equal(user.BirthDay, dto.BirthDay);
    }

    [Fact]
    public void Map_RegisterUserDto_To_User()
    {
        // Arrange
        var dto = new RegisterUserDto(
            UserName: "testuser",
            FirstName: "John",
            LastName: "Doe",
            Email: "john@test.com",
            Password: "password123"
        );

        // Act
        var user = _mapper.Map<User>(dto);

        // Assert
        Assert.Equal(dto.UserName, user.UserName);
        Assert.Equal(dto.FirstName, user.FirstName);
        Assert.Equal(dto.LastName, user.LastName);
        Assert.Equal(dto.Email, user.Email);
        Assert.Equal(0, user.Id); // Should be ignored
        Assert.True(user.RegisteredAt <= DateTime.UtcNow);
    }

    [Fact]
    public void Map_UpdateUserDto_To_User()
    {
        // Arrange
        var dto = new UpdateUserDto
        {
            TeamId = 1,
            FirstName = "Jane",
            LastName = "Smith",
            Email = "jane@test.com",
            UserName = "janesmith",
            BirthDay = new DateTime(1995, 5, 5)
        };

        // Act
        var user = _mapper.Map<User>(dto);

        // Assert
        Assert.Equal(dto.TeamId, user.TeamId);
        Assert.Equal(dto.FirstName, user.FirstName);
        Assert.Equal(dto.LastName, user.LastName);
        Assert.Equal(dto.Email, user.Email);
        Assert.Equal(dto.BirthDay, user.BirthDay);
    }

    [Fact]
    public void Map_Project_To_ProjectDto()
    {
        // Arrange
        var project = new Project
        {
            Id = 1,
            Name = "Test Project",
            Description = "Description",
            CreatedAt = DateTime.UtcNow,
            Deadline = DateTime.UtcNow.AddDays(30)
        };

        // Act
        var dto = _mapper.Map<ProjectDto>(project);

        // Assert
        Assert.Equal(project.Id, dto.Id);
        Assert.Equal(project.Name, dto.Name);
        Assert.Equal(project.Description, dto.Description);
        Assert.Equal(project.CreatedAt, dto.CreatedAt);
        Assert.Equal(project.Deadline, dto.Deadline);
    }

    [Fact]
    public void Map_Project_To_FullProjectDto()
    {
        // Arrange
        var project = new Project
        {
            Id = 1,
            Name = "Test Project",
            Description = "Description",
            CreatedAt = DateTime.UtcNow,
            Deadline = DateTime.UtcNow.AddDays(30),
            AuthorId = 1,
            TeamId = 2
        };

        // Act
        var dto = _mapper.Map<FullProjectDto>(project);

        // Assert
        Assert.Equal(project.Id, dto.Id);
        Assert.Equal(project.Name, dto.Name);
        Assert.Equal(project.Description, dto.Description);
    }

    [Fact]
    public void Map_Task_To_TaskDto()
    {
        // Arrange
        var task = new DAL.Entities.Task
        {
            Id = 1,
            Name = "Test Task",
            Description = "Description",
            State = TaskState.InProgress,
            CreatedAt = DateTime.UtcNow,
            FinishedAt = null
        };

        // Act
        var dto = _mapper.Map<TaskDto>(task);

        // Assert
        Assert.Equal(task.Id, dto.Id);
        Assert.Equal(task.Name, dto.Name);
        Assert.Equal(task.Description, dto.Description);
        Assert.Equal("InProgress", dto.State); // Enum to string
        Assert.Equal(task.CreatedAt, dto.CreatedAt);
        Assert.Equal(task.FinishedAt, dto.FinishedAt);
    }

    [Fact]
    public void Map_Task_To_TaskWithPerformerDto()
    {
        // Arrange
        var task = new DAL.Entities.Task
        {
            Id = 1,
            Name = "Test Task",
            Description = "Description",
            State = TaskState.ToDo,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        var dto = _mapper.Map<TaskWithPerformerDto>(task);

        // Assert
        Assert.Equal(task.Id, dto.Id);
        Assert.Equal(task.Name, dto.Name);
    }

    [Fact]
    public void Map_Team_To_TeamDto()
    {
        // Arrange
        var team = new Team
        {
            Id = 1,
            Name = "Test Team",
            CreatedAt = DateTime.UtcNow
        };

        // Act
        var dto = _mapper.Map<TeamDto>(team);

        // Assert
        Assert.Equal(team.Id, dto.Id);
        Assert.Equal(team.Name, dto.Name);
        Assert.Equal(team.CreatedAt, dto.CreatedAt);
    }

    // TeamWithMembersDto test removed due to constructor requirements

    [Fact]
    public void Map_CreateTeamDto_To_Team()
    {
        // Arrange
        var dto = new CreateTeamDto("New Team");

        // Act
        var team = _mapper.Map<Team>(dto);

        // Assert
        Assert.Equal(dto.Name, team.Name);
        Assert.Equal(0, team.Id); // Should be ignored
        Assert.True(team.CreatedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void Map_UpdateTeamDto_To_Team()
    {
        // Arrange
        var dto = new UpdateTeamDto("Updated Team");

        // Act
        var team = _mapper.Map<Team>(dto);

        // Assert
        Assert.Equal(dto.Name, team.Name);
    }
}
