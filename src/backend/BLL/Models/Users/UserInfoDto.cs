using BLL.Models.Projects;
using BLL.Models.Tasks;

namespace BLL.Models.Users;

public record UserInfoDto(
    UserDto User,
    ProjectDto LastProject,
    int LastProjectTasksCount,
    int NotFinishedOrCanceledTasksCount,
    TaskDto LongestTask);
