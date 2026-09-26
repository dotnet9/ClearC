using ClearC.Core.Models;
using ClearC.Desktop.Infrastructure.Cleanup;
using ClearC.Desktop.Infrastructure.Scanning;

namespace ClearC.Desktop.Tests;

public sealed class CleanupHandlerRegistryTests
{
    [Fact]
    public void Constructor_RequiresAHandlerForEveryCleanerKind()
    {
        var handlers = new ICleanupHandler[]
        {
            new DirectoryContentsHandler(new NullDirectoryCleaner()),
            new FilePatternHandler(new NullDirectoryCleaner()),
            new CommandHandler(new NullProcessRunner(), new NullLockDetector()),
            new RecycleBinHandler(new NullRecycleBinCleaner()),
            new CodexConversationsHandler(new NullCodexCleaner())
        };

        var registry = new CleanupHandlerRegistry(handlers);

        foreach (var kind in Enum.GetValues<CleanerKind>().Where(kind => kind != CleanerKind.None))
        {
            Assert.Equal(kind, registry.Resolve(kind).Kind);
        }
    }

    [Fact]
    public void Constructor_ThrowsWhenAHandlerIsMissing()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new CleanupHandlerRegistry(
        [
            new DirectoryContentsHandler(new NullDirectoryCleaner())
        ]));

        Assert.Contains("缺少清理器", exception.Message);
        Assert.Contains(nameof(CleanerKind.Command), exception.Message);
    }

    [Fact]
    public void Resolve_NeverSilentlyFallsBackForAnalyzeOnlyTargets()
    {
        var registry = new CleanupHandlerRegistry(
        [
            new DirectoryContentsHandler(new NullDirectoryCleaner()),
            new FilePatternHandler(new NullDirectoryCleaner()),
            new CommandHandler(new NullProcessRunner(), new NullLockDetector()),
            new RecycleBinHandler(new NullRecycleBinCleaner()),
            new CodexConversationsHandler(new NullCodexCleaner())
        ]);

        Assert.Throws<InvalidOperationException>(() => registry.Resolve(CleanerKind.None));
    }

    [Fact]
    public async Task FilePatternHandler_DeletesOnlyMatchingFilesAndKeepsDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ClearC.Handlers.{Guid.NewGuid():N}");
        try
        {
            var nested = Path.Combine(root, "nested");
            Directory.CreateDirectory(nested);
            var matching = Path.Combine(root, "thumbcache_32.db");
            var alsoMatching = Path.Combine(nested, "iconcache_48.db");
            var other = Path.Combine(root, "settings.json");
            await File.WriteAllBytesAsync(matching, new byte[16], TestContext.Current.CancellationToken);
            await File.WriteAllBytesAsync(alsoMatching, new byte[32], TestContext.Current.CancellationToken);
            await File.WriteAllBytesAsync(other, new byte[64], TestContext.Current.CancellationToken);

            var target = new CleanupTargetDefinition(
                "thumbnail-icon-cache",
                "缩略图与图标缓存",
                CleanupCategory.GraphicsAndGameCache,
                CleanupRisk.Low,
                "description",
                [root],
                CleanerKind.FilePattern,
                IncludePatterns: ["thumbcache_*.db", "iconcache_*.db"]);
            var item = target.ToItem(112, 2);
            var handler = new FilePatternHandler(new GuardedDirectoryCleaner());

            var result = await handler.CleanAsync(
                new CleanupRequest(item, target),
                TestContext.Current.CancellationToken);

            Assert.Equal(CleanupOutcome.Completed, result.Outcome);
            Assert.Equal(48, result.FreedBytes);
            Assert.False(File.Exists(matching));
            Assert.False(File.Exists(alsoMatching));
            Assert.True(File.Exists(other));
            Assert.True(Directory.Exists(nested));
            Assert.Contains("保留目录结构", result.Message);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task DirectoryContentsHandler_ReportsLeftoverDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ClearC.Handlers.{Guid.NewGuid():N}");
        try
        {
            var keep = Path.Combine(root, "keep");
            Directory.CreateDirectory(keep);
            await File.WriteAllTextAsync(Path.Combine(keep, "in-use.txt"), "x", TestContext.Current.CancellationToken);
            var target = new CleanupTargetDefinition(
                "user-temp",
                "过期临时文件",
                CleanupCategory.TemporaryFiles,
                CleanupRisk.Low,
                "description",
                [root],
                CleanerKind.DirectoryContents,
                MinimumAge: TimeSpan.FromDays(7));
            var item = target.ToItem(1, 1);
            var handler = new DirectoryContentsHandler(new GuardedDirectoryCleaner());

            var result = await handler.CleanAsync(
                new CleanupRequest(item, target),
                TestContext.Current.CancellationToken);

            Assert.Equal(CleanupOutcome.Completed, result.Outcome);
            Assert.Contains("残留 1", result.Message);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task CommandHandler_SkipsTargetsWithoutACommand()
    {
        var target = new CleanupTargetDefinition(
            "npm-cache",
            "npm 缓存",
            CleanupCategory.PackageCache,
            CleanupRisk.Low,
            "description",
            [@"C:\Users\test\AppData\Local\npm-cache"],
            CleanerKind.Command);
        var handler = new CommandHandler(new NullProcessRunner(), new NullLockDetector());

        var result = await handler.CleanAsync(
            new CleanupRequest(target.ToItem(1024, 1), target),
            TestContext.Current.CancellationToken);

        Assert.Equal(CleanupOutcome.Skipped, result.Outcome);
        Assert.Contains("没有配置清理命令", result.Message);
    }

    private sealed class NullDirectoryCleaner : IGuardedDirectoryCleaner
    {
        public Task<DirectoryCleanupResult> CleanContentsAsync(
            DirectoryCleanupRequest request,
            CancellationToken cancellationToken) => Task.FromResult(new DirectoryCleanupResult(0, 0, 0));
    }

    private sealed class NullProcessRunner : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken,
            TimeSpan? timeout = null) => Task.FromResult(new ProcessRunResult(0, string.Empty, string.Empty));
    }

    private sealed class NullLockDetector : ICacheLockDetector
    {
        public Task<CacheLockScanResult> FindLoadedModulesAsync(
            string rootPath,
            CancellationToken cancellationToken = default) => Task.FromResult(new CacheLockScanResult([]));
    }

    private sealed class NullRecycleBinCleaner : IRecycleBinCleaner
    {
        public Task<RecycleBinCleanupResult> EmptyAsync(string driveRoot, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RecycleBinCleanupResult(true));
    }

    private sealed class NullCodexCleaner : ICodexConversationCleaner
    {
        public Task<CodexConversationCleanupResult> CleanAsync(
            IReadOnlyList<string> conversationRoots,
            CancellationToken cancellationToken) => Task.FromResult(new CodexConversationCleanupResult(0, 0, 0));
    }
}
