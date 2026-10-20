using System.ComponentModel.DataAnnotations;
using DAL.Enum;

namespace BLL.Models.Tasks;

// A Board gesture changes only state, preserving historical assignments/settings.
public sealed class UpdateTaskStateRequest
{
    [Required, EnumDataType(typeof(TaskState))]
    public TaskState? State { get; set; }

    public Guid? ExpectedVersion { get; set; }
}
