using System.Globalization;

namespace BLL.Models.Tasks;

public record TaskDto(
    int Id,
    string Name,
    string Description,
    string State,
    DateTime CreatedAt,
    DateTime? FinishedAt)
{
    public override string ToString()
    {
        var finishedAtStr = FinishedAt.HasValue ? FinishedAt.Value.ToString(CultureInfo.InvariantCulture) : "N/A";
        return $"TaskId: {Id}\nTaskName: {Name}\nDescription: {Description}\nState: {State}\nCreatedAt: {CreatedAt}\nFinishedAt: {finishedAtStr}";
    }
}
