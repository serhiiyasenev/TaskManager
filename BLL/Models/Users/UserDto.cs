namespace BLL.Models.Users;

public record UserDto(
    int Id,
    string FirstName,
    string LastName,
    string Email,
    DateTime RegisteredAt,
    DateTime BirthDay);
