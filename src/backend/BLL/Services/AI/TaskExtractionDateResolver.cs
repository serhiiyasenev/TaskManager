#nullable enable
using System.Globalization;
using System.Text.RegularExpressions;

namespace BLL.Services.AI;

public static class TaskExtractionDateResolver
{
    public static (DateTimeOffset? Utc, bool DateOnly) Resolve(string? originalText, DateOnly referenceDate, string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(originalText)) return (null, false);
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException) { return (null, false); }
        var text = originalText.Trim().ToLowerInvariant().Replace('’', '\'');
        text = Regex.Replace(text, @"^(?:до|на|by|on|at)\s+", "", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (Regex.IsMatch(text, @"наступн|next week|after release|після\s|або|\bor\b|\bчи\b|maybe|можливо", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            return (null, false);
        DateOnly? day = text switch
        {
            "today" or "сьогодні" => referenceDate,
            "tomorrow" or "завтра" => referenceDate.AddDays(1),
            "day after tomorrow" or "післязавтра" => referenceDate.AddDays(2),
            _ => null
        };
        if (day is null && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var explicitDay)) day = explicitDay;
        if (day is null)
        {
            var weekday = Weekday(text);
            if (weekday is not null) day = referenceDate.AddDays(((int)weekday - (int)referenceDate.DayOfWeek + 7) % 7);
        }
        if (day is not null) return ToUtc(day.Value.ToDateTime(new TimeOnly(23, 59), DateTimeKind.Unspecified), zone, true);
        // Only explicit ISO date/time can become an exact clock deadline. Vague
        // natural-language times remain editable unresolved values.
        if (Regex.IsMatch(text, @"^\d{4}-\d{2}-\d{2}t\d{2}:\d{2}(?::\d{2})?(?:z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var offset)) return (offset.ToUniversalTime(), false);
        if (DateTime.TryParseExact(text, ["yyyy-MM-dd HH:mm", "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd't'HH:mm"], CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var local)) return ToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone, false);
        return (null, false);
    }

    private static (DateTimeOffset? Utc, bool DateOnly) ToUtc(DateTime local, TimeZoneInfo zone, bool dateOnly)
    {
        if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local)) return (null, false);
        return (new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone)), dateOnly);
    }

    private static DayOfWeek? Weekday(string text) => text switch
    {
        "monday" or "понеділок" or "понеділка" => DayOfWeek.Monday,
        "tuesday" or "вівторок" or "вівторка" => DayOfWeek.Tuesday,
        "wednesday" or "середа" or "середи" or "середу" => DayOfWeek.Wednesday,
        "thursday" or "четвер" or "четверга" => DayOfWeek.Thursday,
        "friday" or "п'ятниця" or "п'ятниці" or "п'ятницю" => DayOfWeek.Friday,
        "saturday" or "субота" or "суботи" or "суботу" => DayOfWeek.Saturday,
        "sunday" or "неділя" or "неділі" or "неділю" => DayOfWeek.Sunday,
        _ => null
    };
}
