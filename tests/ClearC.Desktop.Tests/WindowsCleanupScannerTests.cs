using ClearC.Core.Models;
using ClearC.Desktop.Infrastructure.Cleanup;
using ClearC.Desktop.Infrastructure.Scanning;
using ClearC.Desktop.Infrastructure.Windows;

namespace ClearC.Desktop.Tests;

public sealed class WindowsCleanupScannerTests
{
    [Fact]
    public void Catalog_CodexTargetScansOnlyConversationDirectories()
    {
        var target = new WindowsCleanupTargetCatalog().GetTargets().Single(item => item.Id == "codex-data");

        Assert.Equal(CleanerKind.CodexConversations, target.CleanerKind);
        Assert.Equal(CleanupRisk.High, target.Risk);
        Assert.False(target.IsProtected);
        Assert.Equal(["sessions", "archived_sessions"], target.Paths.Select(Path.GetFileName));
    }

    [Fact]
    public async Task ScanAsync_CombinesResolvedTargetsAndRecycleBin()
    {
        var target = CreateTarget("cache", @"C:\Cache", ScanTier.Fast);
        var scanner = CreateScanner(
            new FakeCatalog(target),
            new FakeSizeCalculator(new(1024, 4)),
            new FakeRecycleProvider(new(2048, 8)));

        var result = await scanner.ScanAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(1024, result.Items[0].SizeBytes);
        Assert.Equal(2048, result.Items[1].SizeBytes);
        Assert.Equal("C:", result.Disk.DriveName);
        Assert.Equal(CleanerKind.RecycleBin, result.Items[1].CleanerKind);
    }

    [Fact]
    public async Task ScanAsync_ReportsPlaceholderThenCompletedItemPerTarget()
    {
        var target = CreateTarget("cache", @"C:\Cache", ScanTier.Fast);
        var progress = new List<ScanProgress>();
        var scanner = CreateScanner(
            new FakeCatalog(target),
            new FakeSizeCalculator(new(1024, 4)),
            new FakeRecycleProvider(new(2048, 8)));

        await scanner.ScanAsync(new CollectingProgress<ScanProgress>(progress.Add), TestContext.Current.CancellationToken);

        var targetEvents = progress.Where(value => value.CurrentTarget == target.DisplayName).ToArray();
        Assert.Equal(2, targetEvents.Length);
        Assert.Null(targetEvents[0].Item);
        Assert.Equal(1024, targetEvents[1].Item!.SizeBytes);
    }

    [Fact]
    public async Task ScanAsync_RunsTheFastTierBeforeTheSlowTier()
    {
        var fast = CreateTarget("fast", @"C:\Fast", ScanTier.Fast);
        var slow = CreateTarget("slow", @"C:\Slow", ScanTier.Slow) with { DisplayName = "Slow" };
        var progress = new List<ScanProgress>();
        var scanner = CreateScanner(
            new FakeCatalog(fast, slow),
            new FakeSizeCalculator(new(1024, 4)),
            new FakeRecycleProvider(default));

        await scanner.ScanAsync(new CollectingProgress<ScanProgress>(progress.Add), TestContext.Current.CancellationToken);

        var slowIndex = progress.FindIndex(value => value.Tier == ScanTier.Slow);
        var fastCompleted = progress.FindLastIndex(value => value.CurrentTarget == fast.DisplayName && value.IsItemCompleted);
        Assert.True(slowIndex > fastCompleted, "慢档必须在快档完成之后才开始。");
    }

    [Fact]
    public async Task ScanAsync_DegradesTimedOutTargetsToZeroBytesWithANote()
    {
        var target = CreateTarget("cache", @"C:\Cache", ScanTier.Fast) with
        {
            ScanTimeout = TimeSpan.FromMilliseconds(20)
        };
        var scanner = CreateScanner(
            new FakeCatalog(target),
            new HangingSizeCalculator(),
            new FakeRecycleProvider(default));

        var result = await scanner.ScanAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, result.Items[0].SizeBytes);
        Assert.Equal("分析超时", result.Items[0].ScanNote);
    }

    [Fact]
    public async Task ScanAsync_MarksProbeFailuresInsteadOfThrowing()
    {
        var target = CreateTarget("cache", @"C:\Cache", ScanTier.Fast);
        var scanner = CreateScanner(
            new FakeCatalog(target),
            new ThrowingSizeCalculator(),
            new FakeRecycleProvider(default));

        var result = await scanner.ScanAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, result.Items[0].SizeBytes);
        Assert.Contains("分析失败", result.Items[0].ScanNote);
    }

    /// <summary>取消发生在慢档时，扫描中止；已通过进度事件交付的快档行由调用方保留。</summary>
    [Fact]
    public async Task ScanAsync_StopsWhenCancelledAndLeavesDeliveredRowsToTheCaller()
    {
        var fast = CreateTarget("fast", @"C:\Fast", ScanTier.Fast);
        var slow = CreateTarget("slow", @"C:\Slow", ScanTier.Slow) with { DisplayName = "Slow" };
        using var cancellation = new CancellationTokenSource();
        var progress = new List<ScanProgress>();
        var scanner = CreateScanner(
            new FakeCatalog(fast, slow),
            new FakeSizeCalculator(new(1024, 4)),
            new FakeRecycleProvider(default));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scanner.ScanAsync(
            new CollectingProgress<ScanProgress>(value =>
            {
                progress.Add(value);
                if (value.Tier == ScanTier.Slow)
                {
                    cancellation.Cancel();
                }
            }),
            cancellation.Token));

        Assert.Contains(progress, value => value.CurrentTarget == fast.DisplayName && value.IsItemCompleted);
    }

    [Fact]
    public async Task ScanAsync_ReturnsNoTargetsOffWindows()
    {
        var target = CreateTarget("cache", @"C:\Cache", ScanTier.Fast);
        var scanner = new WindowsCleanupScanner(
            new FakeCatalog(target),
            CreateProbes(new FakeSizeCalculator(default)),
            new FakeDiskProvider(),
            new FakeRecycleProvider(default),
            new FakePlatform(false));

        var result = await scanner.ScanAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(result.Items);
        Assert.Equal("recycle-bin", result.Items[0].Id);
    }

    /// <summary>快速模式：不跑 DISM / vssadmin，两个分析项不进结果也不进进度。</summary>
    [Fact]
    public async Task ScanAsync_SkipsExternalAnalysisTargetsInFastMode()
    {
        var cache = CreateTarget("cache", @"C:\Cache", ScanTier.Fast);
        var winsxs = CreateTarget("winsxs", @"C:\Windows\WinSxS", ScanTier.Slow, ScanKind.DismAnalyze);
        var vss = CreateTarget("vss-shadow", @"C:\Shadow", ScanTier.Slow, ScanKind.VssQuery) with { DisplayName = "卷影副本" };
        var scanner = CreateScanner(
            new FakeCatalog(cache, winsxs, vss),
            new FakeSizeCalculator(new(1024, 4)),
            new FakeRecycleProvider(default));
        var progress = new List<ScanProgress>();

        var result = await scanner.ScanAsync(
            new CollectingProgress<ScanProgress>(progress.Add),
            TestContext.Current.CancellationToken,
            skipSystemAnalysis: true);

        Assert.Equal(["cache", "recycle-bin"], result.Items.Select(item => item.Id));
        Assert.DoesNotContain(progress, value => value.CurrentTarget is "winsxs" or "卷影副本");
    }

    /// <summary>关闭快速模式后，分析项照旧参与扫描（未提权时给出需管理员说明）。</summary>
    [Fact]
    public async Task ScanAsync_KeepsExternalAnalysisTargetsWithoutFastMode()
    {
        var winsxs = CreateTarget("winsxs", @"C:\Windows\WinSxS", ScanTier.Slow, ScanKind.DismAnalyze);
        var scanner = CreateScanner(
            new FakeCatalog(winsxs),
            new FakeSizeCalculator(new(2048, 4)),
            new FakeRecycleProvider(default));

        var result = await scanner.ScanAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            skipSystemAnalysis: false);

        Assert.Contains(result.Items, item => item.Id == "winsxs");
    }

    /// <summary>勾选的盘符范围原样传给目录解析；多盘扫描靠目录按范围生成目标。</summary>
    [Fact]
    public async Task ScanAsync_PassesTheSelectedDriveScopeToTheCatalog()
    {
        var target = CreateTarget("cache", @"C:\Cache", ScanTier.Fast);
        var catalog = new FakeCatalog(target);
        var scanner = CreateScanner(
            catalog,
            new FakeSizeCalculator(new(1024, 4)),
            new FakeRecycleProvider(default));

        await scanner.ScanAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            driveScope: ["C:", "D:"]);

        Assert.Equal(["C:", "D:"], catalog.LastDriveScope);
    }

    /// <summary>额外盘符的回收站：目录给出分盘回收行时，扫描逐盘用 Shell 数字产出条目。</summary>
    [Fact]
    public async Task ScanAsync_AddsAPerDriveRecycleBinForExtraDrives()
    {
        var dRecycle = new CleanupTargetDefinition(
            "d-recycle-bin", "回收站（D:）", CleanupCategory.RecycleBin, CleanupRisk.Medium,
            "description", [@"D:\$Recycle.Bin"],
            CleanerKind.RecycleBin, LocationOverride: @"D:\$Recycle.Bin");
        var catalog = new FakeCatalog(dRecycle);
        var scanner = CreateScanner(
            catalog,
            new FakeSizeCalculator(default),
            new FakeRecycleProvider(new(4096, 6)));

        var result = await scanner.ScanAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            driveScope: ["C:", "D:"]);

        Assert.Equal(["recycle-bin", "d-recycle-bin"], result.Items.Select(item => item.Id).ToArray());
        Assert.Equal(4096, result.Items.Single(item => item.Id == "d-recycle-bin").SizeBytes);
        Assert.Equal(@"D:\$Recycle.Bin", result.Items.Single(item => item.Id == "d-recycle-bin").Location);
    }

    private static WindowsCleanupScanner CreateScanner(
        ICleanupTargetCatalog catalog,
        IDirectorySizeCalculator sizeCalculator,
        IRecycleBinInfoProvider recycleProvider) => new(
        catalog,
        CreateProbes(sizeCalculator),
        new FakeDiskProvider(),
        recycleProvider,
        new FakePlatform(true));

    private static ScanProbeRegistry CreateProbes(IDirectorySizeCalculator sizeCalculator) => new(
    [
        new DirectorySizeProbe(sizeCalculator),
        new FilePatternProbe(sizeCalculator),
        new SingleFileProbe(sizeCalculator),
        new DismProbe(sizeCalculator, new FakeProcessRunner(), new FakeElevationService()),
        new VssProbe(new FakeProcessRunner(), new FakeElevationService())
    ]);

    private static CleanupTargetDefinition CreateTarget(
        string id,
        string path,
        ScanTier tier,
        ScanKind scanKind = ScanKind.Directory) => new(
        id,
        id,
        CleanupCategory.PackageCache,
        CleanupRisk.Low,
        "description",
        [path],
        CleanerKind.DirectoryContents,
        ScanKind: scanKind,
        Tier: tier);

    private sealed class FakeCatalog(params CleanupTargetDefinition[] targets) : ICleanupTargetCatalog
    {
        public IReadOnlyList<string>? LastDriveScope { get; private set; }

        public IReadOnlyList<CleanupTargetDefinition> GetTargets() => targets;

        public Task<IReadOnlyList<CleanupTargetDefinition>> ResolveTargetsAsync(
            IReadOnlyList<string>? driveScope = null,
            CancellationToken cancellationToken = default)
        {
            LastDriveScope = driveScope;
            return Task.FromResult<IReadOnlyList<CleanupTargetDefinition>>(targets);
        }
    }

    private sealed class FakeSizeCalculator(DirectorySize size) : IDirectorySizeCalculator
    {
        public Task<DirectorySize> CalculateAsync(
            DirectorySizeRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(size);
    }

    /// <summary>永不返回，用来触发每目标扫描超时。</summary>
    private sealed class HangingSizeCalculator : IDirectorySizeCalculator
    {
        public async Task<DirectorySize> CalculateAsync(
            DirectorySizeRequest request,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return default;
        }
    }

    private sealed class ThrowingSizeCalculator : IDirectorySizeCalculator
    {
        public Task<DirectorySize> CalculateAsync(
            DirectorySizeRequest request,
            CancellationToken cancellationToken = default) =>
            throw new IOException("拒绝访问。");
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken,
            TimeSpan? timeout = null) => Task.FromResult(new ProcessRunResult(-1, string.Empty, string.Empty));
    }

    private sealed class FakeElevationService : IElevationService
    {
        public bool IsElevated => false;

        public bool RestartElevated() => false;
    }

    private sealed class FakeDiskProvider : IDiskInfoProvider
    {
        public DiskSnapshot GetSystemDrive() => new("C:", "NTFS", 100_000, 40_000);

        public IReadOnlyList<DiskSnapshot> GetFixedDrives() => [GetSystemDrive(), new("D:", "NTFS", 200_000, 10_000)];
    }

    private sealed class FakeRecycleProvider(DirectorySize size) : IRecycleBinInfoProvider
    {
        public Task<DirectorySize> GetInfoAsync(string driveRoot, CancellationToken cancellationToken = default) =>
            Task.FromResult(size);
    }

    private sealed class FakePlatform(bool isWindows) : IPlatform
    {
        public bool IsWindows { get; } = isWindows;
    }

    private sealed class CollectingProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
