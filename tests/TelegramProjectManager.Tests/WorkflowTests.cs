using Microsoft.EntityFrameworkCore;
using Npgsql;
using TelegramProjectManager.Domain;
using TelegramProjectManager.Services;
using TelegramProjectManager.Telegram;

namespace TelegramProjectManager.Tests;

public sealed class WorkflowTests : PostgresTestHost
{
    [PostgresFact]
    public async Task Unauthorized_groups_private_chats_bots_and_nonconsenting_members_are_not_stored()
    {
        await Send(-999, 1, "/start");
        await Process(Input(-100, 1, "/start") with { IsGroup = false });
        Assert.Equal(0, await WithDb(db => db.Projects.CountAsync()));
        await SetupProject();
        await Send(-100, 99, "private text");
        await Process(Input(-100, 2, "bot text") with { IsBot = true });
        await Send(-100, 2, "opted-in text");
        Assert.Equal("opted-in text", await WithDb(db => db.Messages.Select(m => m.Text).SingleAsync()));
    }

    [PostgresFact]
    public async Task Duplicate_updates_and_reanalysis_cannot_create_duplicate_messages_or_tasks()
    {
        await SetupProject();
        var message = Input(-100, 2, "I will interview ACME tomorrow.");
        await Process(message); await Process(message);
        await Send(-100, 1, "/analyze");
        Clock.Now = Clock.Now.AddHours(1);
        await Send(-100, 1, "/analyze");
        Assert.Equal(1, await WithDb(db => db.Messages.CountAsync()));
        Assert.Equal(1, await WithDb(db => db.Tasks.CountAsync()));
        var task = await WithDb(db => db.Tasks.SingleAsync());
        Assert.Equal(WorkStatus.Proposed, task.Status); Assert.Null(task.OwnerId);
        Assert.Equal(2, Ai.Calls.Count);
        // Even after retention prunes the update ledger, the durable cursor rejects stale updates.
        await WithDb(async db => { await db.ProcessedUpdates.ExecuteDeleteAsync(); return true; });
        await Process(message);
        Assert.Equal(1, await WithDb(db => db.Messages.CountAsync()));
    }

    [PostgresFact]
    public async Task Nominee_accepts_then_updates_work_with_complete_audit_and_blocker_resolution()
    {
        await SetupProject(); await Propose(); await Drain();
        var proposalId = await WithDb(db => db.Notifications.Where(n => n.WorkItemId != null).Select(n => n.TelegramMessageId).SingleAsync());
        await Process(Input(-100, 1, callback: "t:1:Confirmed", messageId: (int)proposalId!));
        Assert.Equal(WorkStatus.Proposed, await WithDb(db => db.Tasks.Select(t => t.Status).SingleAsync()));
        await Process(Input(-100, 2, callback: "t:1:Confirmed", messageId: (int)proposalId!));
        await Send(-100, 1, "/done 1"); // manager is not owner
        Assert.Equal(WorkStatus.Confirmed, await WithDb(db => db.Tasks.Select(t => t.Status).SingleAsync()));
        await Send(-100, 2, "/due 1 2026-11-06T15:00:00+01:00");
        await Send(-100, 2, "/doing 1");
        await Send(-100, 2, "/blocked 1 Waiting for contact");
        Assert.True(await WithDb(db => db.Blockers.AnyAsync(b => b.IsConfirmed && !b.IsResolved)));
        await Send(-100, 2, "/doing 1"); await Send(-100, 2, "/done 1");
        var task = await WithDb(db => db.Tasks.SingleAsync());
        Assert.Equal(WorkStatus.Completed, task.Status);
        Assert.Equal(DateTimeOffset.Parse("2026-11-06T14:00:00Z"), task.DueAt);
        Assert.False(await WithDb(db => db.Blockers.AnyAsync(b => !b.IsResolved)));
        Assert.Equal(7, await WithDb(db => db.TaskUpdates.CountAsync()));
    }

    [PostgresFact]
    public async Task Cross_group_callbacks_and_unknown_bot_messages_cannot_mutate_tasks()
    {
        await SetupProject(); await Propose(); await Drain();
        await Send(-200, 3, "/start"); await Send(-200, 3, "/join"); await Send(-200, 2, "/join");
        var proposalId = await WithDb(db => db.Notifications.Where(n => n.WorkItemId != null).Select(n => n.TelegramMessageId).SingleAsync());
        await Process(Input(-200, 2, callback: "t:1:Confirmed", messageId: (int)proposalId!));
        await Process(Input(-100, 2, callback: "t:1:Confirmed", messageId: 99999));
        Assert.Equal(WorkStatus.Proposed, await WithDb(db => db.Tasks.Select(t => t.Status).SingleAsync()));
        await Send(-200, 2, "/tasks");
        Assert.True(await WithDb(db => db.Notifications.AnyAsync(n => n.Text == "No matching active tasks." &&
            n.ProjectId == db.Projects.Single(p => p.ChatId == -200).Id)));
    }

    [PostgresFact]
    public async Task Composite_foreign_keys_reject_cross_project_owners()
    {
        await SetupProject(); await Propose();
        await Send(-200, 3, "/start"); await Send(-200, 3, "/join");
        var foreignMember = await WithDb(db => db.Members.SingleAsync(m => m.TelegramUserId == 3));
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => WithDb(async db =>
        {
            var task = await db.Tasks.SingleAsync(); task.OwnerId = foreignMember.Id;
            await db.SaveChangesAsync(); return true;
        }));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
    }

    [PostgresFact]
    public async Task Leaving_removes_raw_messages_and_AI_batches_and_stops_further_collection()
    {
        await SetupProject(); await Propose();
        Assert.Equal(1, await WithDb(db => db.AiSuggestions.CountAsync()));
        await Send(-100, 2, "/leave");
        await Send(-100, 2, "Do not collect this.");
        Assert.Equal(0, await WithDb(db => db.Messages.CountAsync()));
        Assert.Equal(0, await WithDb(db => db.AiSuggestions.CountAsync()));
        Assert.False(await WithDb(db => db.Members.Where(m => m.TelegramUserId == 2).Select(m => m.IsActive).SingleAsync()));
    }

    [PostgresFact]
    public async Task Reminders_respect_quiet_hours_daily_budget_and_repeated_ticks()
    {
        await SetupProject(); await Propose(); await Drain();
        await WithDb(async db =>
        {
            var task = await db.Tasks.SingleAsync(); var member = await db.Members.SingleAsync(m => m.TelegramUserId == 2);
            task.OwnerId = member.Id; task.Status = WorkStatus.Confirmed; task.DueAt = Clock.Now.AddHours(2);
            await db.SaveChangesAsync(); return true;
        });
        AiOptions.Enabled = false;
        Clock.Now = DateTimeOffset.Parse("2026-11-05T20:00:00Z"); await Tick();
        Assert.Equal(0, await WithDb(db => db.Notifications.CountAsync(n => n.Kind == "proactive")));
        Clock.Now = DateTimeOffset.Parse("2026-11-06T08:00:00Z"); await Tick(); await Tick();
        Assert.Equal(1, await WithDb(db => db.Notifications.CountAsync(n => n.Kind == "proactive")));
        await Drain();
        Assert.Contains(Telegram.Sent, m => m.Text.Contains("@member2") && m.Text.Contains("overdue"));
        Clock.Now = DateTimeOffset.Parse("2026-11-06T16:00:00Z"); await Tick(); await Tick();
        Assert.Equal(3, await WithDb(db => db.Notifications.CountAsync(n => n.Kind == "proactive")));
        Assert.True(await WithDb(db => db.Notifications.AnyAsync(n => n.DeduplicationKey.StartsWith("weekly:"))));
    }

    [PostgresFact]
    public async Task Outbox_retries_explicit_rate_limits_but_never_retries_ambiguous_sends()
    {
        await Send(-100, 1, "/start");
        Telegram.Failure = new TelegramDeliveryException(429, 5);
        await Dispatch();
        Assert.Equal(DeliveryState.Pending, await WithDb(db => db.Notifications.Select(n => n.State).SingleAsync()));
        Clock.Now = Clock.Now.AddSeconds(6); Telegram.Failure = null;
        Assert.True(await Dispatch()); Assert.False(await Dispatch()); Assert.Single(Telegram.Sent);
        await Send(-100, 1, "/help");
        Telegram.Failure = new TelegramDeliveryException(null);
        await Dispatch(); Telegram.Failure = null;
        Assert.False(await Dispatch());
        Assert.True(await WithDb(db => db.Notifications.AnyAsync(n => n.State == DeliveryState.Uncertain)));
        Assert.Single(Telegram.Sent);
    }

    [PostgresFact]
    public async Task Message_and_analysis_retention_are_enforced_even_when_management_is_disabled()
    {
        await SetupProject(); await Propose();
        Clock.Now = Clock.Now.AddDays(15); Management.Enabled = false;
        await Tick();
        Assert.Equal(0, await WithDb(db => db.Messages.CountAsync()));
        Assert.Equal(0, await WithDb(db => db.AiSuggestions.CountAsync()));
        Assert.Equal(1, await WithDb(db => db.Tasks.CountAsync()));
    }
    [PostgresFact]
    public async Task Proactive_budget_limits_new_work_and_delivery_across_ticks()
    {
        await SetupProject(); await Propose(); await Drain();
        AiOptions.Enabled = false; Management.MaxProactivePerDay = 1;
        await WithDb(async db =>
        {
            var task = await db.Tasks.SingleAsync();
            task.OwnerId = await db.Members.Where(m => m.TelegramUserId == 2).Select(m => m.Id).SingleAsync();
            task.Status = WorkStatus.Confirmed; task.DueAt = Clock.Now.AddHours(1);
            await db.SaveChangesAsync(); return true;
        });
        Clock.Now = DateTimeOffset.Parse("2026-11-06T16:00:00Z");
        await Tick(); await Tick(); await Drain();
        Assert.Equal(1, await WithDb(db => db.Notifications.CountAsync(n => n.Kind == "proactive")));
        Assert.Equal(1, await WithDb(db => db.Notifications.CountAsync(n => n.Kind == "proactive" && n.State == DeliveryState.Sent)));
    }

    [PostgresFact]
    public async Task Invalid_AI_result_is_rejected_without_creating_tasks_and_uses_cooldown()
    {
        await SetupProject();
        await Send(-100, 2, "I will interview ACME.");
        Ai.Result = _ => new TelegramProjectManager.AI.AnalysisResult
        {
            Tasks = [new() { Title = "Invented", Description = "", OwnerTelegramUserId = 999, DueAt = null,
                Priority = "Normal", SourceMessageId = 999, Confidence = 0.9, MissingInformation = [] }],
            Decisions = [], Blockers = []
        };
        await Send(-100, 1, "/analyze"); await Send(-100, 1, "/analyze");
        Assert.Equal(0, await WithDb(db => db.Tasks.CountAsync())); Assert.Single(Ai.Calls);
        Assert.True(await WithDb(db => db.Notifications.AnyAsync(n => n.Text.Contains("invalid data"))));
    }

    [PostgresFact]
    public async Task Preconsent_and_expired_messages_are_never_collected_or_analyzed()
    {
        await SetupProject();
        await Process(Input(-100, 2, "Before consent") with { SentAt = Clock.Now.AddMinutes(-1) });
        await Send(-100, 2, "Current conversation");
        await WithDb(async db =>
        {
            var project = await db.Projects.SingleAsync();
            db.Messages.Add(new ChatMessage { ProjectId = project.Id, TelegramMessageId = 900,
                TelegramUserId = 2, Text = "Expired", SentAt = Clock.Now.AddDays(-20) });
            await db.SaveChangesAsync(); return true;
        });
        await Send(-100, 1, "/analyze");
        Assert.Equal("Current conversation", Assert.Single(Assert.Single(Ai.Calls).Messages).Text);
    }

}
