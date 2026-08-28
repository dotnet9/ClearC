using ClearC.Core.Models;

namespace ClearC.Core.Safety;

public sealed class CleanupSafetyPolicy
{
    private static readonly string[] ProtectedDirectoryNames =
    [
        ".codex",
        ".git",
        "Desktop",
        "Documents",
        "Downloads",
        "OneDrive",
        "source",
        "sources",
        "repos"
    ];

    public SafetyDecision Evaluate(CleanupItem item, bool riskAcknowledged)
    {
        if (!item.CanClean)
        {
            return new(SafetyDecisionKind.Denied, "此项目仅供分析，未提供清理操作。");
        }

        if (item.IsProtected ||
            ContainsProtectedDirectory(item.Location) && !IsCodexConversationCleaner(item))
        {
            return new(SafetyDecisionKind.Denied, "路径属于用户数据或源码保护范围。");
        }

        if (item.Risk != CleanupRisk.Low && !riskAcknowledged)
        {
            return new(SafetyDecisionKind.ConfirmationRequired, "该项目有不可恢复或需要重新下载的影响，必须单独确认。");
        }

        return new(SafetyDecisionKind.Allowed, string.Empty);
    }

    public IReadOnlyList<CleanupItem> BuildPlan(
        IEnumerable<CleanupItem> items,
        ISet<string> selectedIds,
        ISet<string> acknowledgedIds)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(selectedIds);
        ArgumentNullException.ThrowIfNull(acknowledgedIds);

        var plan = new List<CleanupItem>();
        foreach (var item in items.Where(item => selectedIds.Contains(item.Id)))
        {
            var decision = Evaluate(item, acknowledgedIds.Contains(item.Id));
            if (decision.Kind != SafetyDecisionKind.Allowed)
            {
                throw new InvalidOperationException($"{item.DisplayName}: {decision.Reason}");
            }

            plan.Add(item);
        }

        return plan;
    }

    internal static bool ContainsProtectedDirectory(string location)
    {
        if (string.IsNullOrWhiteSpace(location) || !Path.IsPathFullyQualified(location))
        {
            return false;
        }

        var segments = location
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        return segments.Any(segment => ProtectedDirectoryNames.Contains(segment, StringComparer.OrdinalIgnoreCase));
    }

    private static bool IsCodexConversationCleaner(CleanupItem item) =>
        item.Id == "codex-data" &&
        item.CleanerKey == "codex-conversations" &&
        item.Risk == CleanupRisk.High &&
        string.Equals(
            Path.GetFileName(item.Location.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            ".codex",
            StringComparison.OrdinalIgnoreCase);
}
