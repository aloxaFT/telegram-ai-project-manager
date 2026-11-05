using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TelegramProjectManager.Domain;

public static partial class TaskRules
{
    public static bool IsActive(WorkStatus status) => status is not (WorkStatus.Completed or WorkStatus.Cancelled);

    public static bool CanTransition(WorkStatus from, WorkStatus to) => (from, to) switch
    {
        (WorkStatus.Proposed, WorkStatus.Confirmed or WorkStatus.Cancelled) => true,
        (WorkStatus.Confirmed, WorkStatus.InProgress or WorkStatus.Blocked or WorkStatus.Completed or WorkStatus.Cancelled) => true,
        (WorkStatus.InProgress, WorkStatus.Blocked or WorkStatus.Completed or WorkStatus.Cancelled) => true,
        (WorkStatus.Blocked, WorkStatus.InProgress or WorkStatus.Completed or WorkStatus.Cancelled) => true,
        _ => false
    };

    public static bool CanAct(WorkItem task, Guid memberId, bool manager, WorkStatus target)
    {
        if (!CanTransition(task.Status, target)) return false;
        if (task.Status == WorkStatus.Proposed)
            return target == WorkStatus.Cancelled
                ? manager || task.ProposedOwnerId == memberId
                : task.ProposedOwnerId == null || task.ProposedOwnerId == memberId;
        return task.OwnerId == memberId || (manager && target == WorkStatus.Cancelled);
    }

    public static string Fingerprint(string text) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(Whitespace().Replace(text.Trim().Normalize(), " ").ToLowerInvariant())));

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
