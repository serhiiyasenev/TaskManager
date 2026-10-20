namespace BLL.Models.Teams;

public record TeamBasicDto(
    int Id,
    string Name,
    DateTime CreatedAt,
    int? OwnerId = null);
