using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using TelegramProjectManager.Domain;

namespace TelegramProjectManager.AI;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AnalysisResult
{
    public required List<TaskCandidate> Tasks { get; init; }
    public required List<FactCandidate> Decisions { get; init; }
    public required List<FactCandidate> Blockers { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TaskCandidate
{
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required long? OwnerTelegramUserId { get; init; }
    public required string? DueAt { get; init; }
    public required string Priority { get; init; }
    public required long SourceMessageId { get; init; }
    public required double Confidence { get; init; }
    public required string[] MissingInformation { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FactCandidate
{
    public required string Text { get; init; }
    public required long SourceMessageId { get; init; }
}
public sealed record AnalysisContext(DateTimeOffset Now, string TimeZone, string ProjectName,
    IReadOnlyList<ChatMessage> Messages, IReadOnlyList<ProjectMember> Members);

public sealed class AnalysisValidationException : Exception
{
    public AnalysisValidationException() : base("AI output did not satisfy the validated analysis contract.") { }
}

public static partial class AnalysisValidator
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { MaxDepth = 16 };

    public static AnalysisResult Parse(string json, AnalysisContext context)
    {
        try
        {
            if (json.Length > 40000) throw new AnalysisValidationException();
            var result = JsonSerializer.Deserialize<AnalysisResult>(json, JsonOptions) ?? throw new AnalysisValidationException();
            if (result.Tasks == null || result.Decisions == null || result.Blockers == null ||
                result.Tasks.Count > 5 || result.Decisions.Count > 5 || result.Blockers.Count > 5)
                throw new AnalysisValidationException();
            var sources = context.Messages.Select(m => m.TelegramMessageId).ToHashSet();
            var members = context.Members.Where(m => m.IsActive).Select(m => m.TelegramUserId).ToHashSet();
            foreach (var task in result.Tasks)
            {
                if (task == null || !ValidText(task.Title, 200) || !ValidText(task.Description, 2000, true) ||
                    !sources.Contains(task.SourceMessageId) || !double.IsFinite(task.Confidence) || task.Confidence is < 0 or > 1 ||
                    !Enum.TryParse<Priority>(task.Priority, false, out var priority) || !Enum.IsDefined(priority) ||
                    task.Priority != priority.ToString() || task.MissingInformation == null || task.MissingInformation.Length > 8 ||
                    task.MissingInformation.Any(s => !ValidText(s, 100)) ||
                    (task.OwnerTelegramUserId is { } owner && !members.Contains(owner)))
                    throw new AnalysisValidationException();
                if (task.DueAt != null && (!TryDeadline(task.DueAt, out var due) ||
                    due < context.Now.AddDays(-1) || due > context.Now.AddYears(2))) throw new AnalysisValidationException();
            }
            foreach (var fact in result.Decisions.Concat(result.Blockers))
                if (fact == null || !ValidText(fact.Text, 1000) || !sources.Contains(fact.SourceMessageId))
                    throw new AnalysisValidationException();
            return result;
        }
        catch (JsonException) { throw new AnalysisValidationException(); }
    }

    public static bool TryDeadline(string input, out DateTimeOffset due)
    {
        due = default;
        return Timestamp().IsMatch(input) && DateTimeOffset.TryParse(input, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out due);
    }
    private static bool ValidText(string? text, int max, bool empty = false) => text != null && text.Length <= max &&
        (empty || !string.IsNullOrWhiteSpace(text)) && !text.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'));
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|[+-]\d{2}:\d{2})$")]
    private static partial Regex Timestamp();
}
