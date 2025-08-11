namespace Client.Models;

public record ProjectInfoDto(
    ProjectDto Project,
    TaskDto LongestTaskByDescription,
    TaskDto ShortestTaskByName,
    int? TeamMembersCount = null);