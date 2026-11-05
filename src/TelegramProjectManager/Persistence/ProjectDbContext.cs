using Microsoft.EntityFrameworkCore;
using TelegramProjectManager.Domain;

namespace TelegramProjectManager.Persistence;

public sealed class ProjectDbContext(DbContextOptions<ProjectDbContext> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectMember> Members => Set<ProjectMember>();
    public DbSet<ChatMessage> Messages => Set<ChatMessage>();
    public DbSet<WorkItem> Tasks => Set<WorkItem>();
    public DbSet<TaskUpdate> TaskUpdates => Set<TaskUpdate>();
    public DbSet<Decision> Decisions => Set<Decision>();
    public DbSet<Blocker> Blockers => Set<Blocker>();
    public DbSet<AiSuggestion> AiSuggestions => Set<AiSuggestion>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ProcessedUpdate> ProcessedUpdates => Set<ProcessedUpdate>();
    public DbSet<PollingCursor> PollingCursors => Set<PollingCursor>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Project>().HasIndex(x => x.ChatId).IsUnique();
        b.Entity<Project>().Property(x => x.Name).HasMaxLength(200);
        b.Entity<Project>().Property(x => x.Milestones).HasMaxLength(1000);
        b.Entity<ProjectMember>().HasIndex(x => new { x.ProjectId, x.TelegramUserId }).IsUnique();
        b.Entity<ProjectMember>().HasAlternateKey(x => new { x.ProjectId, x.Id });
        b.Entity<ProjectMember>().Property(x => x.DisplayName).HasMaxLength(200);
        b.Entity<ProjectMember>().Property(x => x.Username).HasMaxLength(64);
        b.Entity<ChatMessage>().HasIndex(x => new { x.ProjectId, x.TelegramMessageId }).IsUnique();
        b.Entity<ChatMessage>().HasIndex(x => new { x.ProjectId, x.SentAt });
        b.Entity<ChatMessage>().Property(x => x.Text).HasMaxLength(4000);
        b.Entity<WorkItem>().HasAlternateKey(x => new { x.ProjectId, x.Id });
        b.Entity<WorkItem>().HasIndex(x => new { x.ProjectId, x.Number }).IsUnique();
        b.Entity<WorkItem>().HasIndex(x => new { x.ProjectId, x.Fingerprint }).IsUnique();
        b.Entity<WorkItem>().HasIndex(x => new { x.ProjectId, x.Status, x.DueAt });
        b.Entity<WorkItem>().Property(x => x.Title).HasMaxLength(200);
        b.Entity<WorkItem>().Property(x => x.Description).HasMaxLength(2000);
        b.Entity<WorkItem>().Property(x => x.MissingInformation).HasMaxLength(1000);
        b.Entity<WorkItem>().Property(x => x.Fingerprint).HasMaxLength(64);
        b.Entity<WorkItem>().HasOne<ProjectMember>().WithMany()
            .HasForeignKey(x => new { x.ProjectId, x.OwnerId }).HasPrincipalKey(x => new { x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<WorkItem>().HasOne<ProjectMember>().WithMany()
            .HasForeignKey(x => new { x.ProjectId, x.ProposedOwnerId }).HasPrincipalKey(x => new { x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TaskUpdate>().HasOne<WorkItem>().WithMany()
            .HasForeignKey(x => new { x.ProjectId, x.WorkItemId }).HasPrincipalKey(x => new { x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.Entity<TaskUpdate>().Property(x => x.Note).HasMaxLength(2000);
        b.Entity<TaskUpdate>().HasIndex(x => new { x.ProjectId, x.CreatedAt });
        b.Entity<Blocker>().HasOne<WorkItem>().WithMany()
            .HasForeignKey(x => new { x.ProjectId, x.WorkItemId }).HasPrincipalKey(x => new { x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Blocker>().Property(x => x.Text).HasMaxLength(1000);
        b.Entity<Blocker>().HasIndex(x => new { x.ProjectId, x.Fingerprint }).IsUnique();
        b.Entity<Decision>().Property(x => x.Text).HasMaxLength(1000);
        b.Entity<Decision>().HasIndex(x => new { x.ProjectId, x.Fingerprint }).IsUnique();
        b.Entity<AiSuggestion>().Property(x => x.ValidatedJson).HasColumnType("jsonb");
        b.Entity<AiSuggestion>().HasIndex(x => x.CreatedAt);
        b.Entity<Notification>().HasIndex(x => new { x.ProjectId, x.DeduplicationKey }).IsUnique();
        b.Entity<Notification>().HasIndex(x => new { x.State, x.NotBefore });
        b.Entity<Notification>().Property(x => x.Text).HasMaxLength(4000);
        b.Entity<Notification>().Property(x => x.Kind).HasMaxLength(32);
        b.Entity<Notification>().Property(x => x.DeduplicationKey).HasMaxLength(200);
        b.Entity<Notification>().HasOne<WorkItem>().WithMany()
            .HasForeignKey(x => new { x.ProjectId, x.WorkItemId }).HasPrincipalKey(x => new { x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ProcessedUpdate>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<PollingCursor>().Property(x => x.Id).ValueGeneratedNever();
        foreach (var entity in b.Model.GetEntityTypes().Where(e => e.FindProperty("ProjectId") != null))
            b.Entity(entity.ClrType).HasOne(typeof(Project)).WithMany().HasForeignKey("ProjectId").OnDelete(DeleteBehavior.Cascade);
    }
}
