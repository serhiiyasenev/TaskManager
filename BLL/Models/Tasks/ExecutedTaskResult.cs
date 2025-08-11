using DAL.Entities;

namespace BLL.Models.Tasks;

public class ExecutedTaskResult
{
    public ExecutedTask Task { get; set; }
    public bool PostedMessageToQueueResult { get; set; }
}