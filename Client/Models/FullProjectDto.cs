namespace Client.Models;

public record FullProjectDto(
    int Id,
    string Name,
    string Description,
    DateTime CreatedAt,
    DateTime Deadline,
    List<TaskWithPerformerDto> Tasks,
    UserDto Author,
    TeamDto Team);