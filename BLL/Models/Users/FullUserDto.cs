namespace BLL.Models.Users;

public class FullUserDto
{
    public int Id { get; set; }
    public string Email { get; set; }
    public string UserName { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public DateTime RegisteredAt { get; set; }
}