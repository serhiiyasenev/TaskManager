namespace BLL.Models.Projects;

public record ProjectDto(
    int Id,
    string Name,
    string Description,
    DateTime CreatedAt,
    DateTime Deadline);
