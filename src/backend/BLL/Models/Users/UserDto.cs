namespace BLL.Models.Users;

public class UserDto(
    int id,
    int? teamId,
    string firstName,
    string lastName,
    string email,
    DateTime registeredAt,
    DateTime birthDay)
{
    public int Id { get; init; } = id;
    public int? TeamId { get; init; } = teamId;
    public string FirstName { get; init; } = firstName;
    public string LastName { get; init; } = lastName;
    public string Email { get; init; } = email;
    public DateTime RegisteredAt { get; init; } = registeredAt;
    public DateTime BirthDay { get; init; } = birthDay;
}
