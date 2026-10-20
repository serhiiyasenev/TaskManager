namespace Client.Models;

public record UserInfoDto(
    UserDto User,
    ProjectDto LastProject,
    int LastProjectTasksCount,
    int NotFinishedOrCanceledTasksCount,
    TaskDto LongestTask);