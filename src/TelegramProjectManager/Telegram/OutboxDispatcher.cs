using Microsoft.EntityFrameworkCore;
using TelegramProjectManager.Configuration;
using TelegramProjectManager.Domain;
using TelegramProjectManager.Persistence;
using TelegramProjectManager.Services;

namespace TelegramProjectManager.Telegram;

public sealed class OutboxDispatcher(ProjectDbContext db, ITelegramGateway gateway, TelegramOptions telegram,
    ManagementOptions management, TimeProvider time)
{
    public async Task<bool> DispatchOne(CancellationToken ct)
    {
        Notification? notification = null;
        Project? project = null;
        WorkItem? task = null;
        var now = time.GetUtcNow();
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await DatabaseLock.Acquire(db, ct);
            await db.Notifications.Where(n => n.State == DeliveryState.Sending && n.NotBefore < now.AddMinutes(-2))
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.State, DeliveryState.Uncertain), ct);
            var candidates = await db.Notifications.Where(n => n.State == DeliveryState.Pending && n.NotBefore <= now)
                .OrderBy(n => n.CreatedAt).Take(100).ToListAsync(ct);
            foreach (var candidate in candidates)
            {
                project = await db.Projects.FindAsync([candidate.ProjectId], ct);
                var group = telegram.Groups.SingleOrDefault(g => g.ChatId == project?.ChatId);
                if (group == null || (candidate.Kind == "proactive" && candidate.CreatedAt < now.AddDays(-1)))
                { candidate.State = DeliveryState.Failed; continue; }
                if (candidate.Kind == "proactive")
                {
                    var day = ScheduleRules.DailyKey(now, group.TimeZone);
                    var sent = await db.Notifications.Where(n => n.ProjectId == candidate.ProjectId && n.Kind == "proactive" &&
                        (n.State == DeliveryState.Sent || n.State == DeliveryState.Sending || n.State == DeliveryState.Uncertain) &&
                        (n.SentAt ?? n.NotBefore) >= now.AddDays(-2)).ToListAsync(ct);
                    if (ScheduleRules.IsQuiet(now, group.TimeZone, management) ||
                        sent.Count(n => ScheduleRules.DailyKey(n.SentAt ?? n.NotBefore, group.TimeZone) == day) >= management.MaxProactivePerDay)
                    { candidate.NotBefore = now.AddMinutes(15); continue; }
                    if (candidate.WorkItemId is { } id)
                    {
                        var currentTask = await db.Tasks.SingleOrDefaultAsync(t => t.ProjectId == project!.Id && t.Id == id, ct);
                        if (currentTask == null || !TaskRules.IsActive(currentTask.Status))
                        { candidate.State = DeliveryState.Failed; continue; }
                    }
                }
                notification = candidate;
                candidate.State = DeliveryState.Sending; candidate.Attempts++; candidate.NotBefore = now;
                if (candidate.WorkItemId is { } taskId)
                    task = await db.Tasks.SingleOrDefaultAsync(t => t.ProjectId == candidate.ProjectId && t.Id == taskId, ct);
                break;
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        if (notification == null || project == null) return false;
        try
        {
            notification.TelegramMessageId = await gateway.Send(project.ChatId, notification.Text, task, ct);
            notification.State = DeliveryState.Sent; notification.SentAt = time.GetUtcNow();
        }
        catch (TelegramDeliveryException ex)
        {
            // Telegram explicitly rejected a rate-limited request, so it is safe to retry.
            if (ex.ErrorCode == 429 && notification.Attempts < 5)
            {
                notification.State = DeliveryState.Pending;
                notification.NotBefore = time.GetUtcNow().AddSeconds(Math.Clamp(ex.RetryAfter, 1, 3600));
            }
            else notification.State = ex.ErrorCode is >= 400 and < 500 ? DeliveryState.Failed : DeliveryState.Uncertain;
        }
        // Cancellation/crash after a send leaves Sending, later converted to Uncertain, never blind retry.
        await db.SaveChangesAsync(ct);
        return true;
    }
}

public sealed class OutboxWorker(IServiceScopeFactory scopes, TelegramOptions options, ILogger<OutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<OutboxDispatcher>().DispatchOne(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError("Outbox dispatch failed ({ErrorType}); uncertain sends will not be retried automatically.", ex.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
