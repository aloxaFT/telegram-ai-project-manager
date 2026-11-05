using Microsoft.EntityFrameworkCore;
using TelegramProjectManager.Domain;
using TelegramProjectManager.Persistence;

namespace TelegramProjectManager.Services;

public sealed class Outbox(ProjectDbContext db, TimeProvider time)
{
    public async Task<bool> Queue(Guid projectId, string key, string kind, string text, CancellationToken ct,
        Guid? workItemId = null)
    {
        if (db.Notifications.Local.Any(n => n.ProjectId == projectId && n.DeduplicationKey == key) ||
            await db.Notifications.AnyAsync(n => n.ProjectId == projectId && n.DeduplicationKey == key, ct)) return false;
        db.Notifications.Add(new Notification
        {
            ProjectId = projectId, DeduplicationKey = key, Kind = kind,
            Text = text.Length > 3900 ? text[..3890] + "\n…" : text,
            WorkItemId = workItemId, CreatedAt = time.GetUtcNow(), NotBefore = time.GetUtcNow()
        });
        return true;
    }
}
