using BLL.Models.Users;

namespace BLL.Models.Teams;

public record TeamDetailDto(
    int Id,
    string Name,
    DateTime CreatedAt,
    List<UserBasicDto> Users);
