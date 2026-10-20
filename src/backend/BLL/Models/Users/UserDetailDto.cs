using BLL.Models.Tasks;
using BLL.Models.Teams;

namespace BLL.Models.Users;

public record UserDetailDto(
    int Id,
    string UserName,
    string Email,
    int? TeamId,
    string FirstName,
    string LastName,
    DateTime RegisteredAt,
    DateTime BirthDay,
    TeamBasicDto? Team,
    List<TaskBasicDto> Tasks);
