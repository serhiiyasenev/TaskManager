using BLL.Models.Tasks;
using BLL.Models.Teams;
using BLL.Models.Users;

namespace BLL.Models.Projects;

public record FullProjectDto(
    int Id,
    string Name,
    string Description,
    DateTime CreatedAt,
    DateTime Deadline,
    List<TaskWithPerformerDto> Tasks,
    UserDto Author,
    TeamDto Team);
