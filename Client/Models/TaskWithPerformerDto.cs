namespace Client.Models;

public record TaskWithPerformerDto(
    int Id,
    string Name,
    string Description,
    string State,
    DateTime CreatedAt,
    DateTime? FinishedAt,
    UserDto Performer);