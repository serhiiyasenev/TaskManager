using System.Timers;

namespace Client.Services;

public interface ITimerService
{
    event ElapsedEventHandler Elapsed;
    double Interval { get; set; }
    void Start();
    void Stop();
    void Dispose();
}