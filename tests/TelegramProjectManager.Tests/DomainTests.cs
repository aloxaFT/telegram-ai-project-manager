using TelegramProjectManager.Configuration;
using TelegramProjectManager.Domain;
using TelegramProjectManager.Services;

namespace TelegramProjectManager.Tests;

public sealed class DomainTests
{
    [Fact]
    public void Lifecycle_allows_only_documented_edges_and_terminal_tasks_cannot_reopen()
    {
        var allowed = new Dictionary<WorkStatus, WorkStatus[]>
        {
            [WorkStatus.Proposed] = [WorkStatus.Confirmed, WorkStatus.Cancelled],
            [WorkStatus.Confirmed] = [WorkStatus.InProgress, WorkStatus.Blocked, WorkStatus.Completed, WorkStatus.Cancelled],
            [WorkStatus.InProgress] = [WorkStatus.Blocked, WorkStatus.Completed, WorkStatus.Cancelled],
            [WorkStatus.Blocked] = [WorkStatus.InProgress, WorkStatus.Completed, WorkStatus.Cancelled],
            [WorkStatus.Completed] = [], [WorkStatus.Cancelled] = []
        };
        foreach (var from in Enum.GetValues<WorkStatus>())
        foreach (var to in Enum.GetValues<WorkStatus>())
            Assert.Equal(allowed[from].Contains(to), TaskRules.CanTransition(from, to));
    }

    [Fact]
    public void Manager_cannot_accept_a_nomination_for_someone_else_or_change_their_progress()
    {
        var owner = Guid.NewGuid(); var manager = Guid.NewGuid();
        var task = new WorkItem { ProposedOwnerId = owner };
        Assert.False(TaskRules.CanAct(task, manager, true, WorkStatus.Confirmed));
        Assert.True(TaskRules.CanAct(task, owner, false, WorkStatus.Confirmed));
        Assert.True(TaskRules.CanAct(task, manager, true, WorkStatus.Cancelled));
        task.Status = WorkStatus.Confirmed; task.OwnerId = owner;
        Assert.False(TaskRules.CanAct(task, manager, true, WorkStatus.Completed));
        Assert.True(TaskRules.CanAct(task, owner, false, WorkStatus.Completed));
    }

    [Fact]
    public void Unassigned_proposal_can_be_claimed_but_cannot_be_completed_without_confirmation()
    {
        var task = new WorkItem();
        Assert.True(TaskRules.CanAct(task, Guid.NewGuid(), false, WorkStatus.Confirmed));
        Assert.False(TaskRules.CanAct(task, Guid.NewGuid(), true, WorkStatus.Completed));
    }

    [Theory]
    [InlineData("  Secure  two\n pilots ", "secure two pilots")]
    [InlineData("Interview ACME", "interview acme")]
    public void Canonical_titles_deduplicate_whitespace_and_case(string a, string b) =>
        Assert.Equal(TaskRules.Fingerprint(a), TaskRules.Fingerprint(b));

    [Theory]
    [InlineData("2026-11-05T06:59:00Z", true)]
    [InlineData("2026-11-05T07:00:00Z", false)]
    [InlineData("2026-11-05T19:00:00Z", true)]
    [InlineData("2026-03-29T06:00:00Z", false)]
    [InlineData("2026-10-25T00:30:00Z", true)]
    [InlineData("2026-10-25T01:30:00Z", true)]
    public void Quiet_hours_use_local_time_including_DST(string utc, bool quiet) =>
        Assert.Equal(quiet, ScheduleRules.IsQuiet(DateTimeOffset.Parse(utc), "Europe/Warsaw", new ManagementOptions()));

    [Fact]
    public void Daytime_quiet_window_and_disabled_quiet_window_work()
    {
        var options = new ManagementOptions { QuietStartHour = 12, QuietEndHour = 14 };
        var noon = DateTimeOffset.Parse("2026-11-05T11:00:00Z");
        Assert.True(ScheduleRules.IsQuiet(noon, "Europe/Warsaw", options));
        Assert.False(ScheduleRules.IsQuiet(noon.AddHours(2), "Europe/Warsaw", options));
        options.QuietStartHour = options.QuietEndHour;
        Assert.False(ScheduleRules.IsQuiet(noon, "Europe/Warsaw", options));
    }

    [Fact]
    public void Weekly_and_daily_reports_follow_local_calendar()
    {
        var options = new ManagementOptions();
        var friday = DateTimeOffset.Parse("2026-11-06T16:00:00Z");
        Assert.True(ScheduleRules.WeeklyDue(friday, "Europe/Warsaw", options));
        Assert.True(ScheduleRules.SummaryDue(friday, "Europe/Warsaw", options));
        Assert.False(ScheduleRules.WeeklyDue(friday.AddDays(1), "Europe/Warsaw", options));
        Assert.Equal("2026-11-07", ScheduleRules.DailyKey(friday.AddHours(7), "Europe/Warsaw"));
    }

    [Fact]
    public void Configuration_rejects_unsafe_or_incomplete_enabled_integrations()
    {
        Assert.Throws<InvalidOperationException>(() => ConfigurationValidation.Validate(new() { Enabled = true }, new(), new()));
        Assert.Throws<InvalidOperationException>(() => ConfigurationValidation.Validate(new() { Groups = [new() { ChatId = 123, ManagerUserIds = [1] }] }, new(), new()));
        Assert.Throws<InvalidOperationException>(() => ConfigurationValidation.Validate(new(), new() { Enabled = true, Endpoint = "http://provider.test", ApiKey = "test", Model = "test" }, new()));
        Assert.Throws<InvalidOperationException>(() => ConfigurationValidation.Validate(new(), new(), new() { MessageRetentionDays = 0 }));
    }
}
