using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TelegramProjectManager.AI;
using TelegramProjectManager.Configuration;
using TelegramProjectManager.Domain;
using TelegramProjectManager.Persistence;

namespace TelegramProjectManager.Services;

public sealed class AnalysisCoordinator(ProjectDbContext db, IAiAnalysisService ai, AiOptions options, ManagementOptions management,
    TimeProvider time, Outbox outbox)
{
    public async Task<string> Analyze(Project project, GroupOptions group, string requestKey, bool automatic, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        if (!options.Enabled) return "AI is disabled. Configure the provider securely before using /analyze.";
        if (project.LastAnalyzedAt > now.AddMinutes(-options.AutoAnalyzeMinutes))
            return "Analysis is cooling down; try after the configured analysis interval.";
        var members = await db.Members.Where(m => m.ProjectId == project.Id && m.IsActive).ToListAsync(ct);
        var activeUsers = members.Select(m => m.TelegramUserId).ToArray();
        var raw = await db.Messages.Where(m => m.ProjectId == project.Id && m.SentAt >= now.AddDays(-management.MessageRetentionDays) && activeUsers.Contains(m.TelegramUserId))
            .OrderByDescending(m => m.SentAt).ThenByDescending(m => m.TelegramMessageId).Take(options.MaxMessages).ToListAsync(ct);
        if (automatic && raw.Count(m => project.LastAnalyzedAt == null || m.SentAt > project.LastAnalyzedAt) < options.MinNewMessages)
            return "Not enough new conversation to analyze.";
        var selected = new List<ChatMessage>();
        var length = 0;
        foreach (var message in raw)
        {
            if (length + message.Text.Length > options.MaxCharacters) break;
            selected.Add(message); length += message.Text.Length;
        }
        if (selected.Count == 0) return "No retained, opted-in conversation to analyze.";
        var context = new AnalysisContext(now, group.TimeZone, project.Name, selected.AsEnumerable().Reverse().ToArray(), members);
        // Failures are recorded as an attempted interval, preventing per-tick calls to a broken provider.
        project.LastAnalyzedAt = now;
        AnalysisResult result;
        try { result = AnalysisValidator.Parse(JsonSerializer.Serialize(await ai.Analyze(context, ct), AnalysisValidator.JsonOptions), context); }
        catch (AiUnavailableException) { return "AI is unavailable. No suggestions or assignments were accepted."; }
        catch (AnalysisValidationException) { return "AI returned invalid data. No suggestions or assignments were accepted."; }
        db.AiSuggestions.Add(new AiSuggestion
        {
            ProjectId = project.Id, ValidatedJson = JsonSerializer.Serialize(result, AnalysisValidator.JsonOptions), CreatedAt = now
        });
        var next = (await db.Tasks.Where(t => t.ProjectId == project.Id).MaxAsync(t => (int?)t.Number, ct) ?? 0) + 1;
        var created = 0;
        var seen = (await db.Tasks.Where(t => t.ProjectId == project.Id).Select(t => t.Fingerprint).ToListAsync(ct)).ToHashSet();
        foreach (var candidate in result.Tasks.Where(t => t.Confidence >= options.MinConfidence))
        {
            var fingerprint = TaskRules.Fingerprint(candidate.Title);
            if (!seen.Add(fingerprint)) continue;
            var missing = candidate.MissingInformation.ToList();
            if (candidate.OwnerTelegramUserId == null) missing.Add("Who will take responsibility? Any opted-in member can claim this.");
            if (candidate.DueAt == null) missing.Add("What is the deadline? The accepting owner can set it with /due.");
            var task = new WorkItem
            {
                ProjectId = project.Id, Number = next++, Title = candidate.Title.Trim(), Description = candidate.Description.Trim(),
                Fingerprint = fingerprint, ProposedOwnerId = members.SingleOrDefault(m => m.TelegramUserId == candidate.OwnerTelegramUserId)?.Id,
                DueAt = candidate.DueAt == null ? null : DateTimeOffset.Parse(candidate.DueAt, System.Globalization.CultureInfo.InvariantCulture).ToUniversalTime(),
                Priority = Enum.Parse<Priority>(candidate.Priority), SourceMessageId = candidate.SourceMessageId,
                Confidence = candidate.Confidence, MissingInformation = string.Join("\n", missing).Trim(), CreatedAt = now, UpdatedAt = now
            };
            if (task.MissingInformation.Length > 1000) task.MissingInformation = task.MissingInformation[..1000];
            db.Tasks.Add(task);
            db.TaskUpdates.Add(new TaskUpdate { ProjectId = project.Id, WorkItemId = task.Id, Status = WorkStatus.Proposed,
                CreatedAt = now, Note = "AI proposal; no commitment has been approved." });
            await outbox.Queue(project.Id, requestKey + $":proposal:{task.Number}", automatic ? "proactive" : "interactive",
                ReportService.Detail(task, group, members.ToDictionary(m => m.Id)), ct, task.Id);
            created++;
            if (automatic) break; // At most one proactive proposal per analysis; daily budget applies.
        }
        foreach (var fact in result.Decisions)
        {
            var fingerprint = TaskRules.Fingerprint(fact.Text);
            if (!db.Decisions.Local.Any(d => d.ProjectId == project.Id && d.Fingerprint == fingerprint) &&
                !await db.Decisions.AnyAsync(d => d.ProjectId == project.Id && d.Fingerprint == fingerprint, ct))
                db.Decisions.Add(new Decision { ProjectId = project.Id, Text = fact.Text, SourceMessageId = fact.SourceMessageId, Fingerprint = fingerprint, CreatedAt = now });
        }
        foreach (var fact in result.Blockers)
        {
            var fingerprint = TaskRules.Fingerprint(fact.Text);
            if (!db.Blockers.Local.Any(b => b.ProjectId == project.Id && b.Fingerprint == fingerprint) &&
                !await db.Blockers.AnyAsync(b => b.ProjectId == project.Id && b.Fingerprint == fingerprint, ct))
                db.Blockers.Add(new Blocker { ProjectId = project.Id, Text = fact.Text, SourceMessageId = fact.SourceMessageId, Fingerprint = fingerprint, CreatedAt = now });
        }
        return $"Analysis complete: {created} new task proposals; {result.Decisions.Count} decision observations; {result.Blockers.Count} blocker observations. No assignments were approved.";
    }
}
