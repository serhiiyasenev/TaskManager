using BLL.Models.Tasks;

namespace BLL.Models.Projects;

public record ProjectInfoDto(
    ProjectDto Project,
    TaskDto LongestTaskByDescription,
    TaskDto ShortestTaskByName,
    int? TeamMembersCount = null);
