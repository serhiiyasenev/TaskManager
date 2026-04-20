namespace BLL.Interfaces;

public interface IQueueService
{
    Task<bool> PostValue(string message, string? queueName = null, CancellationToken ct = default);
}
