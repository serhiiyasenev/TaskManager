namespace BLL.Configuration;

public class ReminderOptions
{
    public const string SectionName = "Reminders";

    /// <summary>
    /// Master switch for the scheduler. When false, the hosted service exits immediately.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Interval in minutes between reminder scans.
    /// </summary>
    public int PollIntervalMinutes { get; set; } = 5;

    /// <summary>
    /// Default reminder offset (minutes before due date) when none is provided.
    /// </summary>
    public int DefaultReminderOffsetMinutes { get; set; } = 60;

    /// <summary>
    /// Default escalation delay in minutes after due date.
    /// </summary>
    public int DefaultEscalationDelayMinutes { get; set; } = 60;

    /// <summary>
    /// Optional queue name override for reminder messages. Falls back to RabbitMQ.QueueName when null/empty.
    /// </summary>
    public string? ReminderQueueName { get; set; }
}
