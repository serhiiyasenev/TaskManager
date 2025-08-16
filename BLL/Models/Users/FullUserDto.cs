namespace BLL.Models.Users
{
    public record FullUserDto(
        int Id,
        string Email,
        string UserName,
        string FirstName,
        string LastName,
        DateTime CreatedAt);
}
