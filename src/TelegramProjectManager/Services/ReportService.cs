using Microsoft.EntityFrameworkCore;
using TelegramProjectManager.Configuration;
using TelegramProjectManager.Domain;
using TelegramProjectManager.Persistence;

namespace TelegramProjectManager.Services;

public sealed class ReportService(ProjectDbContext db, TimeProvider time)
{
    public async Task<string> Tasks(Guid projectId, GroupOptions group, bool today, CancellationToken ct)
    {
        var tasks = await db.Tasks.Where(t => t.ProjectId == projectId && t.Status != WorkStatus.Completed && t.Status != WorkStatus.Cancelled)
            .OrderByDescending(t => t.Priority).ThenBy(t => t.DueAt).Take(25).ToListAsync(ct);
        var members = await db.Members.Where(m => m.ProjectId == projectId).ToDictionaryAsync(m => m.Id, ct);
        var date = ScheduleRules.Local(time.GetUtcNow(), group.TimeZone).Date;
        if (today) tasks = tasks.Where(t => t.Status != WorkStatus.Proposed &&
            (t.DueAt == null || ScheduleRules.Local(t.DueAt.Value, group.TimeZone).Date <= date)).ToList();
        return tasks.Count == 0 ? "No matching active tasks." : string.Join("\n", tasks.Select(t =>
            $"#{t.Number} [{t.Status}/{t.Priority}] {t.Title}\n  Owner: {Owner(t, members)}; due: {Due(t, group)}")) +
            "\nUse /task NUMBER for details and buttons (up to 25 tasks shown).";
    }

    public async Task<string> Summary(Project project, GroupOptions group, bool weekly, CancellationToken ct)
    {
        var tasks = await db.Tasks.Where(t => t.ProjectId == project.Id).ToListAsync(ct);
        var since = time.GetUtcNow().AddDays(weekly ? -7 : -1);
        var completed = tasks.Count(t => t.Status == WorkStatus.Completed && t.UpdatedAt >= since);
        var decisions = await db.Decisions.Where(d => d.ProjectId == project.Id).OrderByDescending(d => d.CreatedAt).Take(3).ToListAsync(ct);
        var blockers = await db.Blockers.Where(b => b.ProjectId == project.Id && !b.IsResolved).OrderByDescending(b => b.CreatedAt).Take(5).ToListAsync(ct);
        var recent = await db.TaskUpdates.Where(u => u.ProjectId == project.Id && u.CreatedAt >= since)
            .OrderByDescending(u => u.CreatedAt).Take(8).Select(u => u.Note).ToListAsync(ct);
        return $"{(weekly ? "Weekly report" : "Project summary")}: {project.Name}\nGoals: {project.Milestones}\n" +
               "Milestone counts require evidence from the team; they are not inferred as achieved.\n" +
               $"Active: {tasks.Count(t => TaskRules.IsActive(t.Status) && t.Status != WorkStatus.Proposed)}; " +
               $"proposed: {tasks.Count(t => t.Status == WorkStatus.Proposed)}; completed in period: {completed}; " +
               $"overdue: {tasks.Count(t => TaskRules.IsActive(t.Status) && t.Status != WorkStatus.Proposed && t.DueAt < time.GetUtcNow())}\n" +
               "Blockers:\n" + string.Join("\n", blockers.Select(b => $"- {(b.IsConfirmed ? "Reported" : "AI observation, unconfirmed")}: {b.Text}")) +
               "\nRecent decisions (AI observations, unconfirmed):\n" + string.Join("\n", decisions.Select(d => "- " + d.Text)) +
               (weekly ? "\nRecent activity:\n" + string.Join("\n", recent.Select(n => "- " + n)) : "") +
               "\n\n" + await Tasks(project.Id, group, false, ct);
    }

    public static string Detail(WorkItem t, GroupOptions group, IReadOnlyDictionary<Guid, ProjectMember> members) =>
        $"#{t.Number} {t.Title}\n{t.Description}\nStatus: {t.Status}; priority: {t.Priority}\n" +
        $"Owner: {Owner(t, members)}; due: {Due(t, group)}\nSource message: {t.SourceMessageId}\n" +
        (t.Status == WorkStatus.Proposed ? $"AI confidence: {t.Confidence:P0}. Proposal only; confirmation accepts ownership.\n{t.MissingInformation}\n" : "") +
        (t.DueAt == null ? "Owner: set a deadline with /due NUMBER 2026-12-01T12:00:00+01:00, or leave unscheduled." : "");

    private static string Due(WorkItem t, GroupOptions group) => t.DueAt is { } due
        ? ScheduleRules.Local(due, group.TimeZone).ToString("yyyy-MM-dd HH:mm zzz") : "not set";
    private static string Owner(WorkItem t, IReadOnlyDictionary<Guid, ProjectMember> members)
    {
        var id = t.OwnerId ?? t.ProposedOwnerId;
        if (id is null || !members.TryGetValue(id.Value, out var owner)) return "unassigned";
        return (t.OwnerId == null ? "proposed " : "") + (owner.IsActive ? owner.DisplayName : "former participant");
    }
}
