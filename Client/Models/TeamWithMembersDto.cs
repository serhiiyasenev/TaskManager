using System.Text;

namespace Client.Models;

public record TeamWithMembersDto(
    int Id,
    string Name,
    List<UserDto> Members)
{
    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"TeamId: {Id}");
        sb.AppendLine($"TeamName: {Name}");
        sb.AppendLine($"Team Members:");

        foreach (var member in Members)
        {
            sb.AppendLine(member.ToString());
        }

        return sb.ToString();
    }
}