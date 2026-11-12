using ClearC.Core.I18n;

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

    private static readonly string[] UserProfileContainers =
    [
        "Users",
        "Documents and Settings"
    ];

    private static readonly string[] CodexConversationDirectoryNames =
    [
        "sessions",
        "archived_sessions"
    ];

    private static readonly string[] OneDriveLogCacheSegments =
    [
        "AppData",
        "Local",
        "Microsoft",
        "OneDrive"
    ];

    public SafetyDecision Evaluate(CleanupItem item, bool riskAcknowledged)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (!item.CanClean)
        {
            return new(SafetyDecisionKind.Denied, CoreText.Get(CoreLanguage.CoreSafetyAnalyzeOnly));
        }

        if (item.IsProtected)
        {
            return new(SafetyDecisionKind.Denied, CoreText.Get(CoreLanguage.CoreSafetyProtected));
        }

        if (!IsCodexConversationCleaner(item) && !IsOneDriveLogCache(item))
        {
            var offending = item.CleanRoots.FirstOrDefault(ContainsProtectedDirectory);
            if (offending is not null)
            {
                return new(SafetyDecisionKind.Denied, CoreText.Format(CoreLanguage.CoreSafetyProtectedPath, offending.Trim()));
            }
        }

        if (item.Risk != CleanupRisk.Low && !riskAcknowledged)
        {
            return new(SafetyDecisionKind.ConfirmationRequired, CoreText.Get(CoreLanguage.CoreSafetyConfirm));
        }

        return new(SafetyDecisionKind.Allowed, string.Empty);
    }

    /// <summary>
    /// 逐项给出安全判定，不抛异常。调用方据此把被拒项与原因显示给用户。
    /// </summary>
    public IReadOnlyList<(CleanupItem Item, SafetyDecision Decision)> EvaluateForPlan(
        IEnumerable<CleanupItem> items,
        ISet<string> selectedIds,
        ISet<string> acknowledgedIds)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(selectedIds);
        ArgumentNullException.ThrowIfNull(acknowledgedIds);

        return items
            .Where(item => selectedIds.Contains(item.Id))
            .Select(item => (item, Evaluate(item, acknowledgedIds.Contains(item.Id))))
            .ToArray();
    }

    public IReadOnlyList<CleanupItem> BuildPlan(
        IEnumerable<CleanupItem> items,
        ISet<string> selectedIds,
        ISet<string> acknowledgedIds)
    {
        var evaluations = EvaluateForPlan(items, selectedIds, acknowledgedIds);
        var rejected = evaluations.Where(entry => entry.Decision.Kind != SafetyDecisionKind.Allowed).ToArray();
        if (rejected.Length > 0)
        {
            var reasons = string.Join(
                Environment.NewLine,
                rejected.Select(entry => $"{entry.Item.DisplayName}: {entry.Decision.Reason}"));
            throw new InvalidOperationException(reasons);
        }

        return evaluations.Select(entry => entry.Item).ToArray();
    }

    /// <summary>
    /// 保护判断：忽略盘符根与用户目录前缀后，任意一层目录名命中保护名单即视为受保护。
    /// 这样用户名恰好叫 <c>repos</c> / <c>source</c> 时不会整项被误拒。
    /// </summary>
    internal static bool ContainsProtectedDirectory(string? location)
    {
        if (string.IsNullOrWhiteSpace(location) || !Path.IsPathFullyQualified(location))
        {
            return false;
        }

        var segments = SplitSegments(location);
        var skip = segments.Length > 2 && UserProfileContainers.Contains(segments[1], StringComparer.OrdinalIgnoreCase)
            ? 3
            : 1;

        for (var index = skip; index < segments.Length; index++)
        {
            if (ProtectedDirectoryNames.Contains(segments[index], StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string[] SplitSegments(string location) => location
        .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
        .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

    private static string FileNameOf(string path) =>
        Path.GetFileName(Path.TrimEndingDirectorySeparator(path));

    /// <summary>
    /// OneDrive 客户端自身的日志缓存（<c>AppData\Local\Microsoft\OneDrive</c> 之下）是低风险默认勾选项；
    /// 「OneDrive」保护名单要防的是用户的同步内容目录，不能把该项误拒，否则确认页会一直禁用「开始清理」。
    /// </summary>
    private static bool IsOneDriveLogCache(CleanupItem item) =>
        item.Id == "onedrive-logs" &&
        item.CleanRoots.Count > 0 &&
        item.CleanRoots.All(path => HasSegmentSequence(SplitSegments(path), OneDriveLogCacheSegments));

    private static bool HasSegmentSequence(string[] segments, string[] sequence)
    {
        for (var start = 0; start + sequence.Length <= segments.Length; start++)
        {
            if (segments.Skip(start).Take(sequence.Length)
                .SequenceEqual(sequence, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCodexConversationCleaner(CleanupItem item)
    {
        if (item.Id != "codex-data" ||
            item.CleanerKey != "codex-conversations" ||
            item.Risk != CleanupRisk.High)
        {
            return false;
        }

        if (item.Paths is not { Count: > 0 })
        {
            return string.Equals(FileNameOf(item.Location), ".codex", StringComparison.OrdinalIgnoreCase);
        }

        return item.Paths.All(path =>
            CodexConversationDirectoryNames.Contains(FileNameOf(path), StringComparer.OrdinalIgnoreCase) &&
            string.Equals(
                Path.GetFileName(Directory.GetParent(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))?.FullName ?? string.Empty),
                ".codex",
                StringComparison.OrdinalIgnoreCase));
    }
}
