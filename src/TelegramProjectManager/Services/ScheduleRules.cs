using TelegramProjectManager.Configuration;

namespace TelegramProjectManager.Services;

public static class ScheduleRules
{
    public static DateTimeOffset Local(DateTimeOffset utc, string zone) => TimeZoneInfo.ConvertTime(utc, TimeZoneInfo.FindSystemTimeZoneById(zone));

    public static bool IsQuiet(DateTimeOffset utc, string zone, ManagementOptions options)
    {
        if (options.QuietStartHour == options.QuietEndHour) return false;
        var hour = Local(utc, zone).Hour;
        return options.QuietStartHour < options.QuietEndHour
            ? hour >= options.QuietStartHour && hour < options.QuietEndHour
            : hour >= options.QuietStartHour || hour < options.QuietEndHour;
    }
    public static string DailyKey(DateTimeOffset utc, string zone) => Local(utc, zone).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    public static bool SummaryDue(DateTimeOffset utc, string zone, ManagementOptions options) =>
        !IsQuiet(utc, zone, options) && Local(utc, zone).Hour >= options.DailySummaryHour;
    public static bool WeeklyDue(DateTimeOffset utc, string zone, ManagementOptions options) =>
        !IsQuiet(utc, zone, options) && Local(utc, zone).DayOfWeek == options.WeeklyReportDay && Local(utc, zone).Hour >= options.WeeklyReportHour;
}
