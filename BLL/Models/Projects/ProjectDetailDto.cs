using BLL.Models.Tasks;
using BLL.Models.Teams;
using BLL.Models.Users;

namespace BLL.Models.Projects;

public record ProjectDetailDto(
    int Id,
    int AuthorId,
    int TeamId,
    string Name,
    string Description,
    DateTime CreatedAt,
    DateTime Deadline,
    UserDetailDto Author,
    TeamDetailDto Team,
    List<TaskDetailDto> Tasks);
