using System.Globalization;

namespace TelegramProjectManager.Configuration;

public sealed class TelegramOptions
{
    public bool Enabled { get; set; }
    public string Token { get; set; } = "";
    public List<GroupOptions> Groups { get; set; } = [];
}
public sealed class GroupOptions
{
    public long ChatId { get; set; }
    public string Name { get; set; } = "TradeFlow AI";
    public long[] ManagerUserIds { get; set; } = [];
    public string TimeZone { get; set; } = "Europe/Warsaw";
}
public sealed class AiOptions
{
    public bool Enabled { get; set; }
    public string Provider { get; set; } = "OpenAICompatible";
    public string Endpoint { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "";
    public int MaxMessages { get; set; } = 30;
    public int MaxCharacters { get; set; } = 12000;
    public int AutoAnalyzeMinutes { get; set; } = 15;
    public int MinNewMessages { get; set; } = 5;
    public double MinConfidence { get; set; } = 0.7;
}
public sealed class ManagementOptions
{
    public bool Enabled { get; set; } = true;
    public int TickSeconds { get; set; } = 60;
    public int MessageRetentionDays { get; set; } = 14;
    public int AnalysisRetentionDays { get; set; } = 14;
    public int HistoryRetentionDays { get; set; } = 90;
    public int QuietStartHour { get; set; } = 20;
    public int QuietEndHour { get; set; } = 8;
    public int DailySummaryHour { get; set; } = 17;
    public int WeeklyReportHour { get; set; } = 16;
    public DayOfWeek WeeklyReportDay { get; set; } = DayOfWeek.Friday;
    public int DeadlineLeadHours { get; set; } = 24;
    public int CheckInDays { get; set; } = 2;
    public int MaxProactivePerDay { get; set; } = 4;
}

public static class ConfigurationValidation
{
    public static void Validate(TelegramOptions telegram, AiOptions ai, ManagementOptions management)
    {
        if (telegram.Enabled && (string.IsNullOrWhiteSpace(telegram.Token) || telegram.Groups.Count == 0))
            throw new InvalidOperationException("Enabled Telegram requires Token and at least one authorized Groups entry.");
        if (telegram.Groups.Select(g => g.ChatId).Distinct().Count() != telegram.Groups.Count ||
            telegram.Groups.Any(g => g.ChatId >= 0 || g.ManagerUserIds.Length == 0 || g.ManagerUserIds.Any(id => id <= 0) ||
                                      string.IsNullOrWhiteSpace(g.Name) || g.Name.Length > 200))
            throw new InvalidOperationException("Groups require unique negative chat IDs, a name, and positive manager user IDs.");
        foreach (var group in telegram.Groups) _ = TimeZoneInfo.FindSystemTimeZoneById(group.TimeZone);
        if (ai.Enabled && (ai.Provider != "OpenAICompatible" || string.IsNullOrWhiteSpace(ai.ApiKey) ||
            string.IsNullOrWhiteSpace(ai.Model) || !Uri.TryCreate(ai.Endpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != "https" || !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Query)))
            throw new InvalidOperationException("AI requires Provider=OpenAICompatible, an HTTPS chat-completions Endpoint, ApiKey and Model.");
        if (ai.MaxMessages is < 1 or > 100 || ai.MaxCharacters is < 1000 or > 50000 ||
            ai.AutoAnalyzeMinutes is < 1 or > 1440 || ai.MinNewMessages is < 1 or > 100 ||
            !double.IsFinite(ai.MinConfidence) || ai.MinConfidence is < 0 or > 1)
            throw new InvalidOperationException("Invalid AI limits.");
        if (management.TickSeconds is < 10 or > 3600 || management.MessageRetentionDays is < 1 or > 365 ||
            management.AnalysisRetentionDays is < 1 or > 365 || management.HistoryRetentionDays is < 1 or > 3650 ||
            new[] {management.QuietStartHour, management.QuietEndHour, management.DailySummaryHour, management.WeeklyReportHour}.Any(h => h is < 0 or > 23) ||
            management.DeadlineLeadHours is < 1 or > 168 || management.CheckInDays is < 1 or > 30 ||
            management.MaxProactivePerDay is < 1 or > 20 || !Enum.IsDefined(management.WeeklyReportDay))
            throw new InvalidOperationException("Invalid management schedule/retention limits.");
    }
}
