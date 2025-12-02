using BLL.Models.Users;

namespace BLL.Models.Tasks;

public record TaskDetailDto(
    int Id,
    int ProjectId,
    int PerformerId,
    string Name,
    string Description,
    string State,
    DateTime CreatedAt,
    DateTime? FinishedAt,
    UserBasicDto Performer);
