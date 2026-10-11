using System.ComponentModel.DataAnnotations;
using DAL.Enum;

namespace BLL.Models.Tasks;

public sealed class TaskPageQuery
{
    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;
    [Range(1, 100)]
    public int PageSize { get; set; } = 20;
    [Range(1, int.MaxValue)]
    public int? ProjectId { get; set; }
    [Range(1, int.MaxValue)]
    public int? PerformerId { get; set; }
    [EnumDataType(typeof(TaskState))]
    public TaskState? State { get; set; }
    [MaxLength(100)]
    public string Search { get; set; } = "";
    [RegularExpression("^(due|name|created)$")]
    public string Sort { get; set; } = "created";
    [RegularExpression("^(asc|desc)$")]
    public string Direction { get; set; } = "desc";
    public bool? Overdue { get; set; }
    public DateTimeOffset? DueFrom { get; set; }
    public DateTimeOffset? DueTo { get; set; }
}

public sealed record TaskPageResult(IReadOnlyList<TaskDetailDto> Items, int Page, int PageSize, int TotalCount);
