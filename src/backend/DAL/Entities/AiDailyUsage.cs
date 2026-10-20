namespace DAL.Entities;

public sealed class AiDailyUsage
{
    public string ScopeKey { get; set; } = null!;
    public DateOnly DayUtc { get; set; }
    public long ReservedTokens { get; set; }
    public Guid Version { get; set; }
}
