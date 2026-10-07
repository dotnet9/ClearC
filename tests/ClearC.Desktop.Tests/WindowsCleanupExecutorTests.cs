using ClearC.Core.Models;
using ClearC.Desktop.Infrastructure.Cleanup;
using ClearC.Desktop.Infrastructure.Scanning;
using ClearC.Desktop.Infrastructure.Windows;

namespace ClearC.Desktop.Tests;

public sealed class WindowsCleanupExecutorTests
{
    [Fact]
    public async Task CleanAsync_SkipsNuGetGlobalCacheWhenLoadedModulesAreDetected()
    {
        var target = CreateCommandTarget("nuget-global", ["nuget", "locals", "global-packages", "--clear"], @"C:\Users\test\.nuget\packages");
        var process = new FakeProcessRunner();
        var executor = CreateExecutor(target, process, [new(42, "dotnet", @"C:\Cache\locked.dll")]);

        var result = await executor.CleanAsync(
            [CreateItem(target)], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(CleanupOutcome.Skipped, result.Items[0].Outcome);
        Assert.Contains("PID 42", result.Items[0].Message);
        Assert.Equal(0, process.CallCount);
    }

    [Fact]
    public async Task CleanAsync_MarksOfficialCommandResultsAsEstimated()
    {
        var target = CreateCommandTarget("nuget-http", ["nuget", "locals", "http-cache", "--clear"], @"C:\Users\test\AppData\Local\NuGet\v3-cache");
        var process = new FakeProcessRunner();
        var executor = CreateExecutor(target, process, []);

        var result = await executor.CleanAsync(
            [CreateItem(target)], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(CleanupOutcome.Completed, result.Items[0].Outcome);
        Assert.True(result.Items[0].IsEstimated);
        Assert.Equal(1024, result.Items[0].FreedBytes);
        Assert.Equal(1024, result.EstimatedFreedBytes);
        Assert.Equal(0, result.MeasuredFreedBytes);
        Assert.Equal(1, process.CallCount);
        Assert.Equal(["nuget", "locals", "http-cache", "--clear"], process.Arguments);
    }

    [Fact]
    public async Task CleanAsync_ReportsCommandTimeoutAsFailure()
    {
        var target = CreateCommandTarget("npm-cache", ["cache", "clean", "--force"], @"C:\Users\test\AppData\Local\npm-cache");
        var process = new FakeProcessRunner(new ProcessRunResult(-1, string.Empty, string.Empty, TimedOut: true));
        var executor = CreateExecutor(target, process, []);

        var result = await executor.CleanAsync(
            [CreateItem(target)], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(CleanupOutcome.Failed, result.Items[0].Outcome);
        Assert.Contains("超时", result.Items[0].Message);
        Assert.False(result.Items[0].IsEstimated);
    }

    [Fact]
    public async Task CleanAsync_ReportsMissingCommandAsSkipped()
    {
        var target = CreateCommandTarget("pnpm-store", ["store", "prune"], @"C:\Users\test\AppData\Local\pnpm");
        var process = new FakeProcessRunner(new ProcessRunResult(-1, string.Empty, "系统找不到指定的文件。"));
        var executor = CreateExecutor(target, process, []);

        var result = await executor.CleanAsync(
            [CreateItem(target)], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(CleanupOutcome.Skipped, result.Items[0].Outcome);
        Assert.Contains("未检测到命令", result.Items[0].Message);
    }

    [Fact]
    public async Task CleanAsync_SkipsPathsOutsideAllowedRootsAndNamesThem()
    {
        var target = CreateDirectoryTarget(
            "browser-cache",
            [@"C:\Users\test\AppData\Local\Microsoft\Edge\User Data"],
            [@"C:\Users\test\AppData\Local\Microsoft\Edge\User Data"]);
        var item = CreateItem(target) with { Paths = [@"C:\Windows\System32"] };
        var executor = CreateExecutor(target, new FakeProcessRunner(), []);

        var result = await executor.CleanAsync([item], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(CleanupOutcome.Skipped, result.Items[0].Outcome);
        Assert.Contains(@"C:\Windows\System32", result.Items[0].Message);
        Assert.Contains("不在允许的清理范围内", result.Items[0].Message);
    }

    [Fact]
    public async Task CleanAsync_SkipsItemsWhoseCleanerKindNoLongerMatchesTheWhitelist()
    {
        var target = CreateDirectoryTarget("user-temp", [@"C:\Users\test\AppData\Local\Temp"], [@"C:\Users\test\AppData\Local\Temp"]);
        var item = CreateItem(target) with { CleanerKind = CleanerKind.Command };
        var executor = CreateExecutor(target, new FakeProcessRunner(), []);

        var result = await executor.CleanAsync([item], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(CleanupOutcome.Skipped, result.Items[0].Outcome);
        Assert.Contains("白名单不一致", result.Items[0].Message);
    }

    [Fact]
    public async Task CleanAsync_SkipsUnknownTargets()
    {
        var target = CreateDirectoryTarget("user-temp", [@"C:\Users\test\AppData\Local\Temp"], [@"C:\Users\test\AppData\Local\Temp"]);
        var executor = CreateExecutor(target, new FakeProcessRunner(), []);
        var item = CreateItem(target) with { Id = "not-in-catalog", Paths = [@"C:\Users\test\AppData\Local\Temp"] };

        var result = await executor.CleanAsync([item], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(CleanupOutcome.Skipped, result.Items[0].Outcome);
        Assert.Contains("不在本次启动生成的清理白名单中", result.Items[0].Message);
    }

    [Fact]
    public async Task CleanAsync_SkipsAnalyzeOnlyItems()
    {
        var target = CreateDirectoryTarget("winsxs", [@"C:\Windows\WinSxS"], [@"C:\Windows\WinSxS"]) with
        {
            CleanerKind = CleanerKind.None
        };
        var item = CreateItem(target) with { CleanerKind = CleanerKind.None };
        var executor = CreateExecutor(target, new FakeProcessRunner(), []);

        var result = await executor.CleanAsync([item], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(CleanupOutcome.Skipped, result.Items[0].Outcome);
        Assert.Contains("仅供分析", result.Items[0].Message);
    }

    [Fact]
    public async Task CleanAsync_RejectsElevatedTargetsWhileNotElevated()
    {
        var target = CreateDirectoryTarget("win-temp", [@"C:\Windows\Temp"], [@"C:\Windows\Temp"]) with
        {
            RequiresElevation = true
        };
        var executor = CreateExecutor(target, new FakeProcessRunner(), [], isElevated: false);

        var result = await executor.CleanAsync(
            [CreateItem(target)], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(CleanupOutcome.Skipped, result.Items[0].Outcome);
        Assert.Contains("需要管理员权限", result.Items[0].Message);
    }

    [Fact]
    public async Task CleanAsync_ReportsLeftoverDirectoriesInTheMessage()
    {
        var target = CreateDirectoryTarget("user-temp", [@"C:\Users\test\AppData\Local\Temp"], [@"C:\Users\test\AppData\Local\Temp"]);
        var cleaner = new FakeDirectoryCleaner(new DirectoryCleanupResult(2048, 4, 1, LeftoverDirectories: 2));
        var executor = CreateExecutor(target, new FakeProcessRunner(), [], directoryCleaner: cleaner);

        var result = await executor.CleanAsync(
            [CreateItem(target)], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(CleanupOutcome.Completed, result.Items[0].Outcome);
        Assert.Equal(2048, result.Items[0].FreedBytes);
        Assert.False(result.Items[0].IsEstimated);
        Assert.Contains("残留 2", result.Items[0].Message);
        Assert.Contains("已删除 4", result.Items[0].Message);
        Assert.Contains("跳过 1", result.Items[0].Message);
    }

    [Fact]
    public async Task CleanAsync_UsesLocalCodexConversationCleaner()
    {
        var target = CreateCodexTarget();
        var codexCleaner = new FakeCodexConversationCleaner(
            new CodexConversationCleanupResult(4096, 3, 1));
        var executor = CreateExecutor(target, new FakeProcessRunner(), [], codexCleaner: codexCleaner);

        var result = await executor.CleanAsync(
            [CreateItem(target)], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(CleanupOutcome.Completed, result.Items[0].Outcome);
        Assert.Equal(4096, result.Items[0].FreedBytes);
        Assert.Contains("3 个 Codex 会话文件", result.Items[0].Message);
        Assert.Contains("跳过 1 个", result.Items[0].Message);
        Assert.Equal(1, codexCleaner.CallCount);
        Assert.Equal(target.Paths, codexCleaner.Paths);
    }

    [Fact]
    public async Task CleanAsync_SkipsCodexConversationsWhileCodexIsRunning()
    {
        var target = CreateCodexTarget();
        var codexCleaner = new FakeCodexConversationCleaner(
            new CodexConversationCleanupResult(0, 0, 0, CodexIsRunning: true));
        var executor = CreateExecutor(target, new FakeProcessRunner(), [], codexCleaner: codexCleaner);

        var result = await executor.CleanAsync(
            [CreateItem(target)], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(CleanupOutcome.Skipped, result.Items[0].Outcome);
        Assert.Contains("关闭 Codex", result.Items[0].Message);
    }

    [Fact]
    public async Task CleanAsync_EmptiesOnlyTheRecycleBinOfTheItemLocation()
    {
        var target = CreateDirectoryTarget("recycle-bin", [@"C:\$Recycle.Bin"], [@"C:\$Recycle.Bin"]) with
        {
            CleanerKind = CleanerKind.RecycleBin,
            LocationOverride = @"C:\$Recycle.Bin"
        };
        var recycleBin = new FakeRecycleBinCleaner();
        var executor = CreateExecutor(
            target,
            new FakeProcessRunner(),
            [],
            recycleBinCleaner: recycleBin);

        var result = await executor.CleanAsync(
            [CreateItem(target)], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(CleanupOutcome.Completed, result.Items[0].Outcome);
        Assert.Equal(@"C:\", recycleBin.DriveRoot);
        Assert.False(result.Items[0].IsEstimated);
    }

    [Fact]
    public async Task CleanAsync_StopsAtTheFirstCancelledItemAndKeepsEarlierResults()
    {
        var first = CreateDirectoryTarget("user-temp", [@"C:\Users\test\AppData\Local\Temp"], [@"C:\Users\test\AppData\Local\Temp"]);
        var secondTarget = CreateDirectoryTarget(
            "browser-cache",
            [@"C:\Users\test\AppData\Local\Microsoft\Edge\User Data"],
            [@"C:\Users\test\AppData\Local\Microsoft\Edge\User Data"]);
        var cleaner = new FakeDirectoryCleaner(new DirectoryCleanupResult(1024, 1, 0));
        var executor = CreateExecutor(
            [first, secondTarget],
            new FakeProcessRunner(),
            [],
            directoryCleaner: cleaner);
        using var cancellation = new CancellationTokenSource();
        var progress = new CollectingProgress<CleanupProgress>(value =>
        {
            if (value.Result is not null)
            {
                cancellation.Cancel();
            }
        });

        var result = await executor.CleanAsync(
            [CreateItem(first), CreateItem(secondTarget)],
            progress,
            cancellation.Token);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(CleanupOutcome.Completed, result.Items[0].Outcome);
        Assert.Equal(CleanupOutcome.Cancelled, result.Items[1].Outcome);
        Assert.Contains("用户取消", result.Items[1].Message);
    }

    private static WindowsCleanupExecutor CreateExecutor(
        CleanupTargetDefinition target,
        FakeProcessRunner process,
        IReadOnlyList<CacheLock> locks,
        ICodexConversationCleaner? codexCleaner = null,
        IGuardedDirectoryCleaner? directoryCleaner = null,
        IRecycleBinCleaner? recycleBinCleaner = null,
        bool isElevated = true) => CreateExecutor(
            [target],
            process,
            locks,
            codexCleaner,
            directoryCleaner,
            recycleBinCleaner,
            isElevated);

    private static WindowsCleanupExecutor CreateExecutor(
        IReadOnlyList<CleanupTargetDefinition> targets,
        FakeProcessRunner process,
        IReadOnlyList<CacheLock> locks,
        ICodexConversationCleaner? codexCleaner = null,
        IGuardedDirectoryCleaner? directoryCleaner = null,
        IRecycleBinCleaner? recycleBinCleaner = null,
        bool isElevated = true)
    {
        var cleaner = directoryCleaner ?? new FakeDirectoryCleaner(new DirectoryCleanupResult(1024, 1, 0));
        return new(
            new FakeCatalog(targets),
            new CleanupHandlerRegistry(
            [
                new DirectoryContentsHandler(cleaner),
                new FilePatternHandler(cleaner),
                new CommandHandler(process, new FakeLockDetector(locks)),
                new RecycleBinHandler(recycleBinCleaner ?? new FakeRecycleBinCleaner()),
                new CodexConversationsHandler(codexCleaner ?? new FakeCodexConversationCleaner(new(0, 0, 0)))
            ]),
            new FakeElevationService(isElevated));
    }

    private static CleanupTargetDefinition CreateCommandTarget(string id, string[] arguments, string path) => new(
        id,
        id,
        CleanupCategory.PackageCache,
        CleanupRisk.Low,
        "description",
        [path],
        CleanerKind.Command,
        Command: new("dotnet", arguments, "已清理。"),
        CheckLoadedModules: id == "nuget-global");

    private static CleanupTargetDefinition CreateDirectoryTarget(string id, string[] paths, string[] allowedRoots) => new(
        id,
        id,
        CleanupCategory.PackageCache,
        CleanupRisk.Low,
        "description",
        paths,
        CleanerKind.DirectoryContents,
        allowedRoots);

    private static CleanupTargetDefinition CreateCodexTarget() => new(
        "codex-data",
        "Codex 会话记录",
        CleanupCategory.ApplicationData,
        CleanupRisk.High,
        "description",
        [@"C:\Users\test\.codex\sessions", @"C:\Users\test\.codex\archived_sessions"],
        CleanerKind.CodexConversations,
        [@"C:\Users\test\.codex\sessions", @"C:\Users\test\.codex\archived_sessions"]);

    private static CleanupItem CreateItem(CleanupTargetDefinition target) => new(
        target.Id,
        target.DisplayName,
        target.Location,
        target.Category,
        target.Risk,
        1024,
        1,
        target.Description,
        target.CleanerKind == CleanerKind.None ? null : target.Id,
        CleanerKind: target.CleanerKind,
        Paths: target.Paths);

    private sealed class FakeCatalog(IReadOnlyList<CleanupTargetDefinition> targets) : ICleanupTargetCatalog
    {
        public IReadOnlyList<CleanupTargetDefinition> GetTargets() => targets;

        public Task<IReadOnlyList<CleanupTargetDefinition>> ResolveTargetsAsync(IReadOnlyList<string>? driveScope = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CleanupTargetDefinition>>(targets);
    }

    private sealed class FakeProcessRunner(ProcessRunResult? result = null) : IProcessRunner
    {
        public int CallCount { get; private set; }
        public IReadOnlyList<string> Arguments { get; private set; } = [];
        public TimeSpan? Timeout { get; private set; }

        public Task<ProcessRunResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken,
            TimeSpan? timeout = null)
        {
            CallCount++;
            Arguments = arguments;
            Timeout = timeout;
            return Task.FromResult(result ?? new ProcessRunResult(0, "ok", string.Empty));
        }
    }

    private sealed class FakeDirectoryCleaner(DirectoryCleanupResult result) : IGuardedDirectoryCleaner
    {
        public DirectoryCleanupRequest? Request { get; private set; }

        public Task<DirectoryCleanupResult> CleanContentsAsync(
            DirectoryCleanupRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(result);
        }
    }

    private sealed class FakeLockDetector(IReadOnlyList<CacheLock> locks) : ICacheLockDetector
    {
        public Task<CacheLockScanResult> FindLoadedModulesAsync(
            string rootPath,
            CancellationToken cancellationToken = default) => Task.FromResult(new CacheLockScanResult(locks));
    }

    private sealed class FakeRecycleBinCleaner : IRecycleBinCleaner
    {
        public string? DriveRoot { get; private set; }

        public Task<RecycleBinCleanupResult> EmptyAsync(string driveRoot, CancellationToken cancellationToken = default)
        {
            DriveRoot = driveRoot;
            return Task.FromResult(new RecycleBinCleanupResult(true));
        }
    }

    private sealed class FakeElevationService(bool isElevated) : IElevationService
    {
        public bool IsElevated { get; } = isElevated;

        public bool RestartElevated() => false;
    }

    private sealed class FakeCodexConversationCleaner(CodexConversationCleanupResult result)
        : ICodexConversationCleaner
    {
        public int CallCount { get; private set; }
        public IReadOnlyList<string> Paths { get; private set; } = [];

        public Task<CodexConversationCleanupResult> CleanAsync(
            IReadOnlyList<string> conversationRoots,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Paths = conversationRoots;
            return Task.FromResult(result);
        }
    }

    private sealed class CollectingProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
