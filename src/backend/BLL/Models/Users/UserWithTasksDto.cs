using System.Text;
using BLL.Models.Tasks;

namespace BLL.Models.Users;

public record UserWithTasksDto(
    int Id,
    string FirstName,
    string LastName,
    string Email,
    DateTime RegisteredAt,
    DateTime BirthDay,
    List<TaskDto> Tasks)
{
    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"UserId: {Id}");
        sb.AppendLine($"FirstName: {FirstName}");
        sb.AppendLine($"LastName: {LastName}");
        sb.AppendLine($"Email: {Email}");
        sb.AppendLine($"RegisteredAt: {RegisteredAt}");
        sb.AppendLine($"BirthDay: {BirthDay}");
        sb.AppendLine("Tasks:");

        foreach (var task in Tasks)
        {
            sb.AppendLine(task.ToString());
        }

        return sb.ToString();
    }
}
