using ClearC.Core.Models;
using ClearC.Desktop.I18n;
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
            throw new InvalidOperationException(AppText.Format(Language.RuntimeCleanerMissing, string.Join(", ", missing)));
        }
    }

    public ICleanupHandler Resolve(CleanerKind kind) =>
        _handlers.TryGetValue(kind, out var handler)
            ? handler
            : throw new InvalidOperationException(AppText.Format(Language.RuntimeCleanerNotRegistered, kind));
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
            ? AppText.Get(Language.RuntimeNothingToDelete)
            : AppText.Format(Language.RuntimeDeletedFiles, result.DeletedFiles);
        if (result.SkippedFiles > 0)
        {
            message += AppText.Format(Language.RuntimeSkippedFiles, result.SkippedFiles);
        }

        if (result.LeftoverDirectories > 0)
        {
            message += AppText.Format(Language.RuntimeLeftoverDirectories, result.LeftoverDirectories);
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
            ? AppText.Get(Language.RuntimeNoMatchingCache)
            : AppText.Format(Language.RuntimeDeletedCacheFiles, result.DeletedFiles);
        if (result.SkippedFiles > 0)
        {
            message += AppText.Format(Language.RuntimeSkippedFiles, result.SkippedFiles);
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
            return new(request.Item.Id, CleanupOutcome.Skipped, 0, AppText.Get(Language.RuntimeNoCleanupCommand));
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
                    AppText.Format(Language.RuntimeLoadedCacheModules, processes));
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
            return new(request.Item.Id, CleanupOutcome.Failed, 0, AppText.Get(Language.RuntimeCommandTimedOut));
        }

        if (run.ExitCode == -1 && string.IsNullOrWhiteSpace(run.StandardOutput))
        {
            return new(
                request.Item.Id,
                CleanupOutcome.Skipped,
                0,
                AppText.Format(Language.RuntimeCommandMissing, command.FileName, run.StandardError).Trim());
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
            string.IsNullOrWhiteSpace(details) ? AppText.Format(Language.RuntimeCommandFailed, run.ExitCode) : details);
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
            ? new(request.Item.Id, CleanupOutcome.Completed, request.Item.SizeBytes, AppText.Get(Language.RuntimeRecycleEmptied))
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
                AppText.Get(Language.RuntimeCodexRunning));
        }

        if (cleanup.DeletedFiles == 0)
        {
            return new(request.Item.Id, CleanupOutcome.Skipped, 0, AppText.Get(Language.RuntimeCodexNoFiles));
        }

        var message = cleanup.SkippedFiles == 0
            ? AppText.Format(Language.RuntimeCodexDeleted, cleanup.DeletedFiles)
            : AppText.Format(Language.RuntimeCodexDeletedSkipped, cleanup.DeletedFiles, cleanup.SkippedFiles);
        return new(request.Item.Id, CleanupOutcome.Completed, cleanup.FreedBytes, message);
    }
}
