namespace BLL.Models.Users;

public record UserBasicDto(
    int Id,
    string UserName,
    string Email,
    int? TeamId,
    string FirstName,
    string LastName,
    DateTime RegisteredAt,
    DateTime BirthDay);
