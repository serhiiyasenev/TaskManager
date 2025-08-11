namespace BLL.Interfaces;

public interface IQueueService
{
    Task<bool> PostValue(string message, CancellationToken ct = default);
}