using System.Diagnostics;
using ClearC.Core.Models;
using ClearC.Core.Services;
using ClearC.Desktop.Infrastructure.Scanning;

namespace ClearC.Desktop.Infrastructure.Cleanup;

public sealed class WindowsCleanupExecutor : ICleanupExecutor
{
    private static readonly IReadOnlyDictionary<string, string> NuGetCacheNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["nuget-global"] = "global-packages",
        ["nuget-http"] = "http-cache",
        ["nuget-temp"] = "temp",
        ["nuget-plugins"] = "plugins-cache"
    };

    private readonly IReadOnlyDictionary<string, CleanupTargetDefinition> _targets;
    private readonly IProcessRunner _processRunner;
    private readonly IGuardedDirectoryCleaner _directoryCleaner;
    private readonly ICacheLockDetector _lockDetector;
    private readonly IRecycleBinCleaner _recycleBinCleaner;

    public WindowsCleanupExecutor()
        : this(
            new WindowsCleanupTargetCatalog(),
            new ProcessRunner(),
            new GuardedDirectoryCleaner(),
            new CacheLockDetector(),
            new RecycleBinCleaner())
    {
    }

    internal WindowsCleanupExecutor(
        ICleanupTargetCatalog catalog,
        IProcessRunner processRunner,
        IGuardedDirectoryCleaner directoryCleaner,
        ICacheLockDetector lockDetector,
        IRecycleBinCleaner recycleBinCleaner)
    {
        _targets = catalog.GetTargets().ToDictionary(target => target.Id, StringComparer.Ordinal);
        _processRunner = processRunner;
        _directoryCleaner = directoryCleaner;
        _lockDetector = lockDetector;
        _recycleBinCleaner = recycleBinCleaner;
    }

    public async Task<CleanupResult> CleanAsync(
        IReadOnlyList<CleanupItem> plan,
        IProgress<CleanupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var stopwatch = Stopwatch.StartNew();
        var results = new List<CleanupItemResult>(plan.Count);

        for (var index = 0; index < plan.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = plan[index];
            progress?.Report(new(index, plan.Count, item));

            CleanupItemResult result;
            try
            {
                result = await CleanItemAsync(item, cancellationToken);
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

    private async Task<CleanupItemResult> CleanItemAsync(CleanupItem item, CancellationToken cancellationToken)
    {
        if (item.CleanerKey is null)
        {
            return new(item.Id, CleanupOutcome.Skipped, 0, "此项目仅供分析。");
        }

        if (item.Id == "recycle-bin" && item.CleanerKey == "recycle-bin")
        {
            var driveRoot = Path.GetPathRoot(item.Location) ?? @"C:\";
            return _recycleBinCleaner.Empty(driveRoot, out var error)
                ? new(item.Id, CleanupOutcome.Completed, item.SizeBytes, "回收站已清空。")
                : new(item.Id, CleanupOutcome.Failed, 0, error);
        }

        if (!_targets.TryGetValue(item.Id, out var target) || target.CleanerKey != item.CleanerKey)
        {
            return new(item.Id, CleanupOutcome.Skipped, 0, "目标不在本次启动生成的清理白名单中。");
        }

        if (item.CleanerKey == "nuget-global")
        {
            var locks = target.Paths.SelectMany(_lockDetector.FindLoadedModules).ToArray();
            if (locks.Length > 0)
            {
                var processes = string.Join("、", locks.Select(entry => $"{entry.ProcessName} (PID {entry.ProcessId})").Distinct());
                return new(
                    item.Id,
                    CleanupOutcome.Skipped,
                    0,
                    $"检测到 {processes} 正在加载 NuGet 缓存 DLL。为避免半清理已跳过；关闭相关 IDE 后重新扫描。");
            }
        }

        if (NuGetCacheNames.TryGetValue(item.CleanerKey, out var cacheName))
        {
            var command = await _processRunner.RunAsync(
                "dotnet", ["nuget", "locals", cacheName, "--clear"], cancellationToken);
            return FromCommand(item, command, $"NuGet {cacheName} 已通过官方命令清理。");
        }

        if (item.CleanerKey == "npm-cache")
        {
            var command = await _processRunner.RunAsync("npm", ["cache", "clean", "--force"], cancellationToken);
            return FromCommand(item, command, "npm 缓存已通过官方命令清理。");
        }

        if (item.CleanerKey is "user-temp" or "browser-cache")
        {
            DateTimeOffset? cutoff = target.MinimumAge is null
                ? null
                : DateTimeOffset.UtcNow - target.MinimumAge.Value;
            var result = await _directoryCleaner.CleanContentsAsync(target.Paths, cutoff, cancellationToken);
            var message = result.SkippedFiles == 0
                ? $"已删除 {result.DeletedFiles:N0} 个文件。"
                : $"已删除 {result.DeletedFiles:N0} 个文件，跳过 {result.SkippedFiles:N0} 个占用或无权限文件。";
            return new(item.Id, CleanupOutcome.Completed, result.FreedBytes, message);
        }

        return new(item.Id, CleanupOutcome.Skipped, 0, "没有匹配的安全清理器。");
    }

    private static CleanupItemResult FromCommand(CleanupItem item, ProcessRunResult command, string successMessage)
    {
        if (command.Succeeded)
        {
            return new(item.Id, CleanupOutcome.Completed, item.SizeBytes, successMessage);
        }

        var details = string.Join(" ", new[] { command.StandardError, command.StandardOutput }
            .Where(value => !string.IsNullOrWhiteSpace(value)))
            .ReplaceLineEndings(" ")
            .Trim();
        if (details.Length > 300)
        {
            details = details[..300] + "...";
        }

        return new(
            item.Id,
            CleanupOutcome.Failed,
            0,
            string.IsNullOrWhiteSpace(details) ? $"清理命令失败，退出代码 {command.ExitCode}。" : details);
    }
}
