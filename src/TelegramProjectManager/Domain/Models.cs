namespace TelegramProjectManager.Domain;

public enum WorkStatus { Proposed, Confirmed, InProgress, Blocked, Completed, Cancelled }
public enum Priority { Low, Normal, High, Urgent }
public enum DeliveryState { Pending, Sending, Sent, Failed, Uncertain }

public sealed class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long ChatId { get; set; }
    public string Name { get; set; } = "TradeFlow AI";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastAnalyzedAt { get; set; }
    public string Milestones { get; set; } = "20 discovery interviews; 2 paid pilots; 5 recurring customers";
}

public sealed class ProjectMember
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public long TelegramUserId { get; set; }
    public string DisplayName { get; set; } = "";
    public string? Username { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
}

public sealed class ChatMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public long TelegramMessageId { get; set; }
    public long TelegramUserId { get; set; }
    public string Text { get; set; } = "";
    public DateTimeOffset SentAt { get; set; }
}

public sealed class WorkItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public int Number { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public WorkStatus Status { get; set; } = WorkStatus.Proposed;
    public Priority Priority { get; set; }
    public Guid? ProposedOwnerId { get; set; }
    public Guid? OwnerId { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public long SourceMessageId { get; set; }
    public double Confidence { get; set; }
    public string MissingInformation { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class TaskUpdate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid WorkItemId { get; set; }
    public long? ActorUserId { get; set; }
    public WorkStatus? PreviousStatus { get; set; }
    public WorkStatus Status { get; set; }
    public string Note { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Decision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public string Text { get; set; } = "";
    public long SourceMessageId { get; set; }
    public string Fingerprint { get; set; } = "";
    public bool IsConfirmed { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Blocker
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid? WorkItemId { get; set; }
    public string Text { get; set; } = "";
    public long SourceMessageId { get; set; }
    public string Fingerprint { get; set; } = "";
    public bool IsResolved { get; set; }
    public bool IsConfirmed { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class AiSuggestion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public string ValidatedJson { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

// Durable outbox + notification history. Sending/Uncertain records are not automatically resent.
public sealed class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public string DeduplicationKey { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Text { get; set; } = "";
    public Guid? WorkItemId { get; set; }
    public DeliveryState State { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset NotBefore { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public long? TelegramMessageId { get; set; }
}

public sealed class ProcessedUpdate
{
    public long Id { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}
public sealed class PollingCursor
{
    public int Id { get; set; } = 1;
    public int NextOffset { get; set; }
}
