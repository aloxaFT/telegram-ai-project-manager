using Microsoft.EntityFrameworkCore;
using TelegramProjectManager.Configuration;
using TelegramProjectManager.Domain;
using TelegramProjectManager.Persistence;

namespace TelegramProjectManager.Services;

public sealed class ManagementScheduler(ProjectDbContext db, TelegramOptions telegram, ManagementOptions options,
    AiOptions aiOptions, TimeProvider time, Outbox outbox, ReportService reports, AnalysisCoordinator analysis)
{
    public async Task Tick(CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await DatabaseLock.Acquire(db, ct);
        var now = time.GetUtcNow();
        await db.Messages.Where(m => m.SentAt < now.AddDays(-options.MessageRetentionDays)).ExecuteDeleteAsync(ct);
        await db.AiSuggestions.Where(s => s.CreatedAt < now.AddDays(-options.AnalysisRetentionDays)).ExecuteDeleteAsync(ct);
        await db.ProcessedUpdates.Where(u => u.ProcessedAt < now.AddDays(-options.HistoryRetentionDays)).ExecuteDeleteAsync(ct);
        // Keep active task history; prune old terminal-task audit records and notification bodies.
        var terminal = db.Tasks.Where(t => t.Status == WorkStatus.Completed || t.Status == WorkStatus.Cancelled).Select(t => t.Id);
        await db.TaskUpdates.Where(u => u.CreatedAt < now.AddDays(-options.HistoryRetentionDays) && terminal.Contains(u.WorkItemId)).ExecuteDeleteAsync(ct);
        await db.Notifications.Where(n => n.CreatedAt < now.AddDays(-options.HistoryRetentionDays)).ExecuteDeleteAsync(ct);
        foreach (var group in telegram.Groups)
        {
            var project = await db.Projects.SingleOrDefaultAsync(p => p.ChatId == group.ChatId, ct);
            if (project == null || !telegram.Enabled || !options.Enabled || ScheduleRules.IsQuiet(now, group.TimeZone, options)) continue;
            var day = ScheduleRules.DailyKey(now, group.TimeZone);
            var recent = await db.Notifications.Where(n => n.ProjectId == project.Id && n.Kind == "proactive" && n.CreatedAt >= now.AddDays(-2)).ToListAsync(ct);
            var used = recent.Count(n => ScheduleRules.DailyKey(n.CreatedAt, group.TimeZone) == day);
            var budget = options.MaxProactivePerDay - used;
            async Task Queue(string key, string text, Guid? taskId = null)
            {
                if (budget > 0 && await outbox.Queue(project.Id, key, "proactive", text, ct, taskId)) budget--;
            }
            if (ScheduleRules.WeeklyDue(now, group.TimeZone, options))
                await Queue("weekly:" + day, await reports.Summary(project, group, true, ct));
            if (ScheduleRules.SummaryDue(now, group.TimeZone, options))
                await Queue("daily:" + day, await reports.Summary(project, group, false, ct));
            var tasks = await db.Tasks.Where(t => t.ProjectId == project.Id && t.Status != WorkStatus.Proposed &&
                    t.Status != WorkStatus.Completed && t.Status != WorkStatus.Cancelled && t.OwnerId != null)
                .OrderBy(t => t.DueAt).ToListAsync(ct);
            var members = await db.Members.Where(m => m.ProjectId == project.Id && m.IsActive).ToDictionaryAsync(m => m.Id, ct);
            foreach (var task in tasks)
            {
                if (budget <= 0) break;
                if (!members.TryGetValue(task.OwnerId!.Value, out var owner)) continue;
                var name = owner.Username == null ? owner.DisplayName : "@" + owner.Username;
                if (task.DueAt is { } due && due <= now.AddHours(options.DeadlineLeadHours))
                    await Queue($"deadline:{task.Id:N}:{due.UtcTicks}:{day}",
                        $"{name}: #{task.Number} {task.Title} is {(due <= now ? "overdue" : "due soon")} ({ScheduleRules.Local(due, group.TimeZone):yyyy-MM-dd HH:mm zzz}). Please update progress with /task {task.Number}.", task.Id);
                else if (task.UpdatedAt <= now.AddDays(-options.CheckInDays))
                {
                    // Stable bucket per last update prevents daily check-ins until the configured cadence elapses.
                    var bucket = (long)((now - task.UpdatedAt).TotalDays / options.CheckInDays);
                    await Queue($"checkin:{task.Id:N}:{task.UpdatedAt.UtcTicks}:{bucket}",
                        $"{name}: any progress or blockers on #{task.Number} {task.Title}? Use /task {task.Number} to update.", task.Id);
                }
            }
            if (budget > 0 && aiOptions.Enabled && (project.LastAnalyzedAt == null || project.LastAnalyzedAt <= now.AddMinutes(-aiOptions.AutoAnalyzeMinutes)))
                _ = await analysis.Analyze(project, group, $"auto:{now.UtcTicks}", true, ct);
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}

public sealed class ManagementWorker(IServiceScopeFactory scopes, ManagementOptions options,
    ILogger<ManagementWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.TickSeconds));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ManagementScheduler>().Tick(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError("Management tick failed ({ErrorType}); retrying on next tick.", ex.GetType().Name); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
