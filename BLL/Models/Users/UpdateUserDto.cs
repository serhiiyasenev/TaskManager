namespace BLL.Models.Users;

public class UpdateUserDto
{
    public int? TeamId { get; set; }
    public string Email { get; set; }
    public string UserName { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public DateTime BirthDay { get; set; }
}