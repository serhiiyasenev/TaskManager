using System.Timers;
using Timer = System.Timers.Timer;

namespace Client.Services
{
    public class TimerService : ITimerService
    {
        private Timer _timer;

        public TimerService(double interval)
        {
            _timer = new Timer(interval);
            _timer.Elapsed += OnElapsed;
        }

        public double Interval
        {
            get { return _timer.Interval; }
            set { _timer.Interval = value; }
        }

        public event ElapsedEventHandler? Elapsed;

        public void Start()
        {
            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
        }

        public void Dispose()
        {
            _timer.Elapsed -= OnElapsed;
            _timer.Dispose();
        }

        private void OnElapsed(object sender, ElapsedEventArgs e)
        {
            Elapsed?.Invoke(sender, e);
        }
    }
}
