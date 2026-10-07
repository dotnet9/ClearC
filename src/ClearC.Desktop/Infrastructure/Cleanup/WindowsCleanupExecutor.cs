using System.Diagnostics;
using ClearC.Core.Models;
using ClearC.Core.Services;
using ClearC.Desktop.Infrastructure.Scanning;
using ClearC.Desktop.Infrastructure.Windows;

namespace ClearC.Desktop.Infrastructure.Cleanup;

public sealed class WindowsCleanupExecutor : ICleanupExecutor
{
    private readonly ICleanupTargetCatalog _catalog;
    private readonly CleanupHandlerRegistry _handlers;
    private readonly IElevationService _elevation;

    public WindowsCleanupExecutor()
        : this(new WindowsCleanupTargetCatalog(), CreateHandlers(), new ElevationService())
    {
    }

    private static CleanupHandlerRegistry CreateHandlers()
    {
        var processRunner = new ProcessRunner();
        var directoryCleaner = new GuardedDirectoryCleaner();
        var lockDetector = new CacheLockDetector();
        return new CleanupHandlerRegistry(
        [
            new DirectoryContentsHandler(directoryCleaner),
            new FilePatternHandler(directoryCleaner),
            new CommandHandler(processRunner, lockDetector),
            new RecycleBinHandler(new RecycleBinCleaner()),
            new CodexConversationsHandler(new CodexConversationCleaner())
        ]);
    }

    internal WindowsCleanupExecutor(
        ICleanupTargetCatalog catalog,
        CleanupHandlerRegistry handlers,
        IElevationService elevation)
    {
        _catalog = catalog;
        _handlers = handlers;
        _elevation = elevation;
    }

    public async Task<CleanupResult> CleanAsync(
        IReadOnlyList<CleanupItem> plan,
        IProgress<CleanupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var stopwatch = Stopwatch.StartNew();
        var results = new List<CleanupItemResult>(plan.Count);

        // 与扫描同一代目录状态：执行前按计划涉及的盘符重新解析一次目录/探测结果。
        var scope = plan
            .SelectMany(item => item.CleanRoots)
            .Select(root => Path.GetPathRoot(root)?.TrimEnd('\\', '/'))
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var targets = BuildTargetMap(await _catalog.ResolveTargetsAsync(scope, cancellationToken));

        for (var index = 0; index < plan.Count; index++)
        {
            var item = plan[index];
            progress?.Report(new(index, plan.Count, item));

            CleanupItemResult result;
            try
            {
                // 取消检查放在 try 内：取消时仍为当前项留下 Cancelled 结果，已完成项的结果不会丢。
                cancellationToken.ThrowIfCancellationRequested();
                result = await CleanItemAsync(item, targets, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                result = new(item.Id, CleanupOutcome.Cancelled, 0, "用户取消，当前项之后的任务未执行。");
                results.Add(result);
                progress?.Report(new(index + 1, plan.Count, item, result));
                break;
            }
            catch (Exception exception)
            {
                result = new(item.Id, CleanupOutcome.Failed, 0, exception.Message);
            }

            results.Add(result);
            progress?.Report(new(index + 1, plan.Count, item, result));
        }

        stopwatch.Stop();
        return new(results, stopwatch.Elapsed);
    }

    private IReadOnlyDictionary<string, CleanupTargetDefinition> BuildTargetMap(
        IReadOnlyList<CleanupTargetDefinition> discovered)
    {
        var map = _catalog.GetTargets().ToDictionary(target => target.Id, StringComparer.Ordinal);
        foreach (var target in discovered)
        {
            // 探测到的路径优先：用户改了 GRADLE_USER_HOME / 换了 Steam 库时按最新路径校验。
            map[target.Id] = target;
        }

        return map;
    }

    private async Task<CleanupItemResult> CleanItemAsync(
        CleanupItem item,
        IReadOnlyDictionary<string, CleanupTargetDefinition> targets,
        CancellationToken cancellationToken)
    {
        if (item.CleanerKind == CleanerKind.None)
        {
            return new(item.Id, CleanupOutcome.Skipped, 0, "此项目仅供分析。");
        }

        if (!targets.TryGetValue(item.Id, out var target))
        {
            return new(item.Id, CleanupOutcome.Skipped, 0, "目标不在本次启动生成的清理白名单中。");
        }

        if (target.CleanerKind != item.CleanerKind)
        {
            return new(item.Id, CleanupOutcome.Skipped, 0, "目标清理方式与本次启动的白名单不一致。");
        }

        var outside = item.CleanRoots.FirstOrDefault(root => !IsAllowed(root, target.EffectiveAllowedRoots));
        if (outside is not null)
        {
            return new(item.Id, CleanupOutcome.Skipped, 0, $"路径不在允许的清理范围内：{outside}");
        }

        if (target.RequiresElevation && !_elevation.IsElevated)
        {
            return new(item.Id, CleanupOutcome.Skipped, 0, "需要管理员权限：请点击「以管理员重启」后重试。");
        }

        var handler = _handlers.Resolve(item.CleanerKind);
        return await handler.CleanAsync(new CleanupRequest(item, target), cancellationToken);
    }

    private static bool IsAllowed(string candidate, IReadOnlyList<string> allowedRoots)
    {
        if (string.IsNullOrWhiteSpace(candidate) || !Path.IsPathFullyQualified(candidate))
        {
            return false;
        }

        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        foreach (var root in allowedRoots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
            {
                continue;
            }

            var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            if (string.Equals(normalized, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
