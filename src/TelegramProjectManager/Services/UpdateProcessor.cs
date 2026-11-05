using Microsoft.EntityFrameworkCore;
using TelegramProjectManager.AI;
using TelegramProjectManager.Configuration;
using TelegramProjectManager.Domain;
using TelegramProjectManager.Persistence;

namespace TelegramProjectManager.Services;

public sealed record IncomingUpdate(int UpdateId, long ChatId, bool IsGroup, long UserId, string DisplayName,
    string? Username, int MessageId, string? Text = null, string? CallbackData = null, bool IsBot = false, DateTimeOffset? SentAt = null);

public static class DatabaseLock
{
    // Serialize local state changes across worker replicas. Telegram polling also has a separate leader lock.
    public static Task Acquire(ProjectDbContext db, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(763819401)", ct);
}

public sealed class UpdateProcessor(ProjectDbContext db, TelegramOptions options, TimeProvider time,
    Outbox outbox, ReportService reports, AnalysisCoordinator analysis, ManagementOptions management)
{
    private const string Help = """
        /start — manager setup; /join — consent to message storage and AI analysis
        /leave — withdraw consent and remove retained raw messages
        /tasks — active work; /today — priorities; /task NUMBER — details and inline actions
        /analyze — analyze opted-in discussion (manager); /status — counts; /report — summary
        /done NUMBER — complete; /doing NUMBER — start/resume; /blocked NUMBER reason — report blocker
        /cancel NUMBER — cancel; /due NUMBER ISO_TIMESTAMP — set deadline (owner)
        /blockers — unresolved blockers; /resolve ID — resolve an observed blocker (manager)
        /help — commands. Proposed tasks require the nominee's acceptance; an unassigned proposal can be claimed.
        """;

    public async Task Process(IncomingUpdate input, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await DatabaseLock.Acquire(db, ct);
        var cursor = await db.PollingCursors.FindAsync([1], ct);
        if ((cursor == null || input.UpdateId >= cursor.NextOffset) &&
            !await db.ProcessedUpdates.AnyAsync(u => u.Id == input.UpdateId, ct))
        {
            await Handle(input, ct);
            db.ProcessedUpdates.Add(new ProcessedUpdate { Id = input.UpdateId, ProcessedAt = time.GetUtcNow() });
        }
        if (cursor == null) db.PollingCursors.Add(new PollingCursor { NextOffset = checked(input.UpdateId + 1) });
        else cursor.NextOffset = Math.Max(cursor.NextOffset, checked(input.UpdateId + 1));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task Handle(IncomingUpdate input, CancellationToken ct)
    {
        var group = options.Groups.SingleOrDefault(g => g.ChatId == input.ChatId);
        if (group == null || !input.IsGroup || input.IsBot || input.UserId <= 0) return;
        var commandParts = (input.Text ?? "").Split(' ', 3, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var command = commandParts.FirstOrDefault()?.Split('@')[0].ToLowerInvariant();
        var manager = group.ManagerUserIds.Contains(input.UserId);
        var now = time.GetUtcNow();
        var sentAt = input.SentAt ?? now;
        var project = await db.Projects.SingleOrDefaultAsync(p => p.ChatId == input.ChatId, ct);
        if (project == null)
        {
            if (command != "/start" || !manager || input.CallbackData != null) return;
            project = new Project { ChatId = input.ChatId, Name = group.Name, CreatedAt = now };
            db.Projects.Add(project);
            await db.SaveChangesAsync(ct);
        }
        var key = $"update:{input.UpdateId}";
        Task<bool> Reply(string text, Guid? taskId = null) => outbox.Queue(project.Id, key, "interactive", text, ct, taskId);
        if (input.CallbackData == null && command is "/start" or "/help")
        {
            await Reply(command == "/start"
                ? $"{project.Name} is authorized. Goals: {project.Milestones}.\nObtain all participants' informed consent before disabling privacy mode. Only members who send /join will be stored/analyzed. AI suggestions are never assignments.\n" + Help : Help);
            return;
        }
        var member = await db.Members.SingleOrDefaultAsync(m => m.ProjectId == project.Id && m.TelegramUserId == input.UserId, ct);
        if (input.CallbackData == null && command == "/join")
        {
            if (member == null)
            {
                member = new ProjectMember { ProjectId = project.Id, TelegramUserId = input.UserId, JoinedAt = now };
                db.Members.Add(member);
            }
            member.IsActive = true;
            member.JoinedAt = sentAt;
            member.DisplayName = Truncate(input.DisplayName, 200);
            member.Username = input.Username == null ? null : Truncate(input.Username, 64);
            await Reply("You opted in: future text messages may be retained and sent to the configured AI provider. /leave stops collection and removes your retained raw messages. Approved project records have separate retention rules; see the project privacy policy.");
            return;
        }
        if (member == null || !member.IsActive) return;
        member.DisplayName = Truncate(input.DisplayName, 200);
        member.Username = input.Username == null ? null : Truncate(input.Username, 64);
        if (input.CallbackData != null)
        {
            var parts = input.CallbackData.Split(':');
            if (parts.Length != 3 || parts[0] != "t" || !int.TryParse(parts[1], out var number) ||
                !Enum.TryParse<WorkStatus>(parts[2], false, out var target) || !Enum.IsDefined(target)) return;
            var task = await db.Tasks.SingleOrDefaultAsync(t => t.ProjectId == project.Id && t.Number == number, ct);
            if (task == null || !await db.Notifications.AnyAsync(n => n.ProjectId == project.Id && n.WorkItemId == task.Id &&
                    n.TelegramMessageId == input.MessageId && n.State == DeliveryState.Sent, ct)) return;
            var response = await Transition(project, task, member, manager, target, "Updated using Telegram button.", ct);
            await Reply(response, task.Id);
            return;
        }
        switch (command)
        {
            case "/leave":
                member.IsActive = false; member.DisplayName = "Former participant"; member.Username = null;
                await db.Messages.Where(m => m.ProjectId == project.Id && m.TelegramUserId == input.UserId).ExecuteDeleteAsync(ct);
                // Analysis batches can include several participants: purge all retained batches for this group.
                await db.AiSuggestions.Where(s => s.ProjectId == project.Id).ExecuteDeleteAsync(ct);
                await Reply("Consent withdrawn. Your raw conversation and this group's retained AI analysis batches were removed. Existing tasks/history remain project records. Rejoin with /join.");
                break;
            case "/tasks": await Reply(await reports.Tasks(project.Id, group, false, ct)); break;
            case "/today": await Reply(await reports.Tasks(project.Id, group, true, ct)); break;
            case "/status": case "/report": await Reply(await reports.Summary(project, group, command == "/report", ct)); break;
            case "/analyze":
                await Reply(manager ? await analysis.Analyze(project, group, key, false, ct) : "Only a configured group manager can request analysis.");
                break;
            case "/blockers":
                var blockers = await db.Blockers.Where(b => b.ProjectId == project.Id && !b.IsResolved).OrderByDescending(b => b.CreatedAt).Take(10).ToListAsync(ct);
                await Reply(blockers.Count == 0 ? "No unresolved blockers." : string.Join("\n", blockers.Select(b =>
                    $"{b.Id:N}: {(b.IsConfirmed ? "Reported" : "AI observation (unconfirmed)")} — {b.Text}")) + "\nManager: /resolve ID when addressed.");
                break;
            case "/resolve":
                if (!manager || commandParts.Length < 2 || !Guid.TryParse(commandParts[1], out var blockerId))
                { await Reply("Manager only. Usage: /resolve BLOCKER_ID"); break; }
                var blocker = await db.Blockers.SingleOrDefaultAsync(b => b.ProjectId == project.Id && b.Id == blockerId, ct);
                if (blocker != null) blocker.IsResolved = true;
                await Reply(blocker == null ? "Blocker not found in this project." : "Blocker resolved. Update any related task separately.");
                break;
            case "/task": case "/done": case "/doing": case "/blocked": case "/cancel": case "/due":
                if (commandParts.Length < 2 || !int.TryParse(commandParts[1], out var number))
                { await Reply("Supply a task number, e.g. /task 1."); break; }
                var task = await db.Tasks.SingleOrDefaultAsync(t => t.ProjectId == project.Id && t.Number == number, ct);
                if (task == null) { await Reply("Task not found in this project."); break; }
                if (command == "/task")
                {
                    var members = await db.Members.Where(m => m.ProjectId == project.Id).ToDictionaryAsync(m => m.Id, ct);
                    await Reply(ReportService.Detail(task, group, members), task.Id);
                    break;
                }
                if (command == "/due")
                {
                    if (task.OwnerId != member.Id || !TaskRules.IsActive(task.Status))
                    { await Reply("Only the confirmed owner of an active task can set its deadline."); break; }
                    if (commandParts.Length < 3 || !AnalysisValidator.TryDeadline(commandParts[2], out var due) ||
                        due < now || due > now.AddYears(2))
                    { await Reply("Use /due NUMBER ISO_TIMESTAMP with an explicit offset or Z, within the next two years."); break; }
                    task.DueAt = due.ToUniversalTime(); task.UpdatedAt = now;
                    db.TaskUpdates.Add(new TaskUpdate { ProjectId = project.Id, WorkItemId = task.Id, ActorUserId = input.UserId,
                        Status = task.Status, PreviousStatus = task.Status, CreatedAt = now, Note = $"Deadline set to {task.DueAt:O}" });
                    await Reply($"Deadline updated for #{task.Number}.", task.Id); break;
                }
                var targetStatus = command switch
                {
                    "/done" => WorkStatus.Completed, "/doing" => WorkStatus.InProgress,
                    "/blocked" => WorkStatus.Blocked, _ => WorkStatus.Cancelled
                };
                if (command == "/blocked" && commandParts.Length < 3)
                { await Reply("Usage: /blocked NUMBER reason"); break; }
                await Reply(await Transition(project, task, member, manager, targetStatus,
                    commandParts.Length > 2 ? Truncate(commandParts[2], 1000) : "Updated using Telegram command.", ct), task.Id);
                break;
            default:
                if (input.Text is { Length: > 0 } text && !text.StartsWith('/') && sentAt >= member.JoinedAt &&
                    sentAt >= now.AddDays(-management.MessageRetentionDays) && sentAt <= now.AddMinutes(5) &&
                    !await db.Messages.AnyAsync(m => m.ProjectId == project.Id && m.TelegramMessageId == input.MessageId, ct))
                    db.Messages.Add(new ChatMessage { ProjectId = project.Id, TelegramMessageId = input.MessageId,
                        TelegramUserId = input.UserId, Text = Truncate(text, 4000), SentAt = sentAt });
                break;
        }
    }

    private async Task<string> Transition(Project project, WorkItem task, ProjectMember actor, bool manager,
        WorkStatus target, string note, CancellationToken ct)
    {
        if (!TaskRules.CanAct(task, actor.Id, manager, target))
            return "Not permitted for this owner/status. Only the proposed owner may accept; an unassigned proposal may be claimed. Only owners update accepted work; managers may cancel.";
        var previous = task.Status;
        if (target == WorkStatus.Confirmed) task.OwnerId = actor.Id;
        task.Status = target; task.UpdatedAt = time.GetUtcNow();
        db.TaskUpdates.Add(new TaskUpdate { ProjectId = project.Id, WorkItemId = task.Id, ActorUserId = actor.TelegramUserId,
            PreviousStatus = previous, Status = target, CreatedAt = time.GetUtcNow(), Note = note });
        if (target == WorkStatus.Blocked)
        {
            var fingerprint = TaskRules.Fingerprint($"task:{task.Id}:{note}");
            var existing = await db.Blockers.SingleOrDefaultAsync(b => b.ProjectId == project.Id && b.Fingerprint == fingerprint, ct);
            if (existing == null) db.Blockers.Add(new Blocker { ProjectId = project.Id, WorkItemId = task.Id, Text = note,
                Fingerprint = fingerprint, IsConfirmed = true, CreatedAt = time.GetUtcNow(), SourceMessageId = task.SourceMessageId });
            else existing.IsResolved = false;
        }
        else if (target is WorkStatus.InProgress or WorkStatus.Completed or WorkStatus.Cancelled)
            await db.Blockers.Where(b => b.ProjectId == project.Id && b.WorkItemId == task.Id && !b.IsResolved)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.IsResolved, true), ct);
        return $"#{task.Number} is now {target}." + (target == WorkStatus.Confirmed && task.DueAt == null ? " What is the deadline? Set /due NUMBER ISO_TIMESTAMP." : "");
    }
    private static string Truncate(string text, int max) => text.Length > max ? text[..max] : text;
}
