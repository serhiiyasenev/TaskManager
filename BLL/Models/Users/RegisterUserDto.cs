namespace BLL.Models.Users
{
    public record RegisterUserDto(
        string UserName,
        string FirstName,
        string LastName,
        string Email,
        string Password);
}
