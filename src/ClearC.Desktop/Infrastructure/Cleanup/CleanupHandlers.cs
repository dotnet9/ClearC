using ClearC.Core.Models;
using ClearC.Desktop.Infrastructure.Scanning;

namespace ClearC.Desktop.Infrastructure.Cleanup;

internal sealed record CleanupRequest(CleanupItem Item, CleanupTargetDefinition Target);

internal interface ICleanupHandler
{
    CleanerKind Kind { get; }

    Task<CleanupItemResult> CleanAsync(CleanupRequest request, CancellationToken cancellationToken);
}

internal sealed class CleanupHandlerRegistry
{
    private readonly IReadOnlyDictionary<CleanerKind, ICleanupHandler> _handlers;

    public CleanupHandlerRegistry(IEnumerable<ICleanupHandler> handlers)
    {
        _handlers = handlers.ToDictionary(handler => handler.Kind);
        var missing = Enum.GetValues<CleanerKind>()
            .Where(kind => kind != CleanerKind.None && !_handlers.ContainsKey(kind))
            .ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException($"缺少清理器：{string.Join("、", missing)}");
        }
    }

    public ICleanupHandler Resolve(CleanerKind kind) =>
        _handlers.TryGetValue(kind, out var handler)
            ? handler
            : throw new InvalidOperationException($"没有匹配 {kind} 的安全清理器。");
}

internal sealed class DirectoryContentsHandler(IGuardedDirectoryCleaner directoryCleaner) : ICleanupHandler
{
    public CleanerKind Kind => CleanerKind.DirectoryContents;

    public async Task<CleanupItemResult> CleanAsync(CleanupRequest request, CancellationToken cancellationToken)
    {
        var result = await directoryCleaner.CleanContentsAsync(
            new DirectoryCleanupRequest(
                request.Item.CleanRoots,
                request.Target.MinimumAge is null ? null : DateTimeOffset.UtcNow - request.Target.MinimumAge.Value,
                request.Target.IncludePatterns),
            cancellationToken);

        var message = result.DeletedFiles == 0 && result.SkippedFiles == 0
            ? "没有可删除的内容。"
            : $"已删除 {result.DeletedFiles:N0} 个文件。";
        if (result.SkippedFiles > 0)
        {
            message += $"跳过 {result.SkippedFiles:N0} 个占用或无权限文件。";
        }

        if (result.LeftoverDirectories > 0)
        {
            message += $"残留 {result.LeftoverDirectories:N0} 个非空目录。";
        }

        return new(request.Item.Id, CleanupOutcome.Completed, result.FreedBytes, message);
    }
}

/// <summary>只删除匹配模式的文件，保留目录本身（缩略图缓存等）。</summary>
internal sealed class FilePatternHandler(IGuardedDirectoryCleaner directoryCleaner) : ICleanupHandler
{
    public CleanerKind Kind => CleanerKind.FilePattern;

    public async Task<CleanupItemResult> CleanAsync(CleanupRequest request, CancellationToken cancellationToken)
    {
        var result = await directoryCleaner.CleanContentsAsync(
            new DirectoryCleanupRequest(
                request.Item.CleanRoots,
                null,
                request.Target.IncludePatterns,
                DeleteEmptyDirectories: false),
            cancellationToken);

        var message = result.DeletedFiles == 0
            ? "没有匹配的缓存文件。"
            : $"已删除 {result.DeletedFiles:N0} 个缓存文件，保留目录结构。";
        if (result.SkippedFiles > 0)
        {
            message += $"跳过 {result.SkippedFiles:N0} 个占用或无权限文件。";
        }

        return new(request.Item.Id, CleanupOutcome.Completed, result.FreedBytes, message);
    }
}

/// <summary>
/// 官方命令清理：真实释放量不可知，按扫描时大小标记为估算值。
/// 命令一旦启动就等待其安全结束（不响应取消），避免半清理。
/// </summary>
internal sealed class CommandHandler(
    IProcessRunner processRunner,
    ICacheLockDetector lockDetector) : ICleanupHandler
{
    public CleanerKind Kind => CleanerKind.Command;

    public async Task<CleanupItemResult> CleanAsync(CleanupRequest request, CancellationToken cancellationToken)
    {
        var command = request.Target.Command;
        if (command is null)
        {
            return new(request.Item.Id, CleanupOutcome.Skipped, 0, "目标没有配置清理命令。");
        }

        if (request.Target.CheckLoadedModules)
        {
            var lockScan = await lockDetector.FindLoadedModulesAsync(request.Item.CleanRoots[0], cancellationToken);
            if (lockScan.Locks.Count > 0)
            {
                var processes = string.Join(
                    "、",
                    lockScan.Locks.Select(entry => $"{entry.ProcessName} (PID {entry.ProcessId})").Distinct());
                return new(
                    request.Item.Id,
                    CleanupOutcome.Skipped,
                    0,
                    $"检测到 {processes} 正在加载缓存 DLL。为避免半清理已跳过；关闭相关 IDE 后重新扫描。");
            }
        }

        var run = await processRunner.RunAsync(
            command.FileName,
            command.Arguments,
            CancellationToken.None,
            command.Timeout);

        if (run.Succeeded)
        {
            return new(request.Item.Id, CleanupOutcome.Completed, request.Item.SizeBytes, command.SuccessMessage, IsEstimated: true);
        }

        if (run.TimedOut)
        {
            return new(request.Item.Id, CleanupOutcome.Failed, 0, "命令超时已终止。");
        }

        if (run.ExitCode == -1 && string.IsNullOrWhiteSpace(run.StandardOutput))
        {
            return new(
                request.Item.Id,
                CleanupOutcome.Skipped,
                0,
                $"未检测到命令 {command.FileName}，已跳过。{run.StandardError}".Trim());
        }

        var details = string.Join(" ", new[] { run.StandardError, run.StandardOutput }
            .Where(value => !string.IsNullOrWhiteSpace(value)))
            .ReplaceLineEndings(" ")
            .Trim();
        if (details.Length > 300)
        {
            details = details[..300] + "...";
        }

        return new(
            request.Item.Id,
            CleanupOutcome.Failed,
            0,
            string.IsNullOrWhiteSpace(details) ? $"清理命令失败，退出代码 {run.ExitCode}。" : details);
    }
}

internal sealed class RecycleBinHandler(IRecycleBinCleaner recycleBinCleaner) : ICleanupHandler
{
    public CleanerKind Kind => CleanerKind.RecycleBin;

    public async Task<CleanupItemResult> CleanAsync(CleanupRequest request, CancellationToken cancellationToken)
    {
        var driveRoot = Path.GetPathRoot(request.Item.Location) ?? @"C:\";
        var result = await recycleBinCleaner.EmptyAsync(driveRoot, cancellationToken);
        return result.Succeeded
            ? new(request.Item.Id, CleanupOutcome.Completed, request.Item.SizeBytes, "回收站已清空。")
            : new(request.Item.Id, CleanupOutcome.Failed, 0, result.Error);
    }
}

internal sealed class CodexConversationsHandler(ICodexConversationCleaner codexConversationCleaner) : ICleanupHandler
{
    public CleanerKind Kind => CleanerKind.CodexConversations;

    public async Task<CleanupItemResult> CleanAsync(CleanupRequest request, CancellationToken cancellationToken)
    {
        var cleanup = await codexConversationCleaner.CleanAsync(request.Item.CleanRoots, cancellationToken);
        if (cleanup.FatalError is not null)
        {
            return new(request.Item.Id, CleanupOutcome.Failed, 0, cleanup.FatalError);
        }

        if (cleanup.CodexIsRunning)
        {
            return new(
                request.Item.Id,
                CleanupOutcome.Skipped,
                0,
                "检测到 Codex 正在运行。为避免破坏当前会话，已跳过；请关闭 Codex 后重新扫描清理。");
        }

        if (cleanup.DeletedFiles == 0)
        {
            return new(request.Item.Id, CleanupOutcome.Skipped, 0, "没有可删除的 Codex 会话文件。");
        }

        var message = cleanup.SkippedFiles == 0
            ? $"已永久删除 {cleanup.DeletedFiles:N0} 个 Codex 会话文件。"
            : $"已永久删除 {cleanup.DeletedFiles:N0} 个 Codex 会话文件，跳过 {cleanup.SkippedFiles:N0} 个占用、无权限或重解析点文件。";
        return new(request.Item.Id, CleanupOutcome.Completed, cleanup.FreedBytes, message);
    }
}
