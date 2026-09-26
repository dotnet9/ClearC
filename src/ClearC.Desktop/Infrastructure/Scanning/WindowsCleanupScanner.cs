using System.Collections.Concurrent;
using System.Diagnostics;
using ClearC.Core.Models;
using ClearC.Core.Services;
using ClearC.Desktop.Infrastructure.Cleanup;
using ClearC.Desktop.Infrastructure.Windows;

namespace ClearC.Desktop.Infrastructure.Scanning;

public sealed class WindowsCleanupScanner : ICleanupScanner
{
    private const int FastParallelism = 4;
    private const int SlowParallelism = 2;

    private readonly ICleanupTargetCatalog _catalog;
    private readonly ScanProbeRegistry _probes;
    private readonly IDiskInfoProvider _diskInfoProvider;
    private readonly IRecycleBinInfoProvider _recycleBinInfoProvider;
    private readonly IPlatform _platform;

    public WindowsCleanupScanner()
        : this(
            new WindowsCleanupTargetCatalog(),
            CreateProbes(),
            new WindowsDiskInfoProvider(),
            new WindowsRecycleBinInfoProvider(),
            SystemPlatform.Instance)
    {
    }

    private static ScanProbeRegistry CreateProbes()
    {
        var sizeCalculator = new DirectorySizeCalculator();
        var processRunner = new ProcessRunner();
        var elevation = new ElevationService();
        return new ScanProbeRegistry(
        [
            new DirectorySizeProbe(sizeCalculator),
            new FilePatternProbe(sizeCalculator),
            new SingleFileProbe(sizeCalculator),
            new DismProbe(sizeCalculator, processRunner, elevation),
            new VssProbe(processRunner, elevation)
        ]);
    }

    internal WindowsCleanupScanner(
        ICleanupTargetCatalog catalog,
        ScanProbeRegistry probes,
        IDiskInfoProvider diskInfoProvider,
        IRecycleBinInfoProvider recycleBinInfoProvider,
        IPlatform platform)
    {
        _catalog = catalog;
        _probes = probes;
        _diskInfoProvider = diskInfoProvider;
        _recycleBinInfoProvider = recycleBinInfoProvider;
        _platform = platform;
    }

    public async Task<ScanResult> ScanAsync(
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default,
        bool skipSystemAnalysis = false)
    {
        var stopwatch = Stopwatch.StartNew();
        var disk = _diskInfoProvider.GetSystemDrive();
        var resolved = _platform.IsWindows
            ? await _catalog.ResolveTargetsAsync(cancellationToken)
            : [];
        // 快速模式：不拉起 DISM / vssadmin 子进程，别为两个分析项让整次扫描多等几分钟。
        var targets = skipSystemAnalysis
            ? resolved.Where(target => !RequiresExternalAnalysis(target)).ToArray()
            : resolved;
        var total = targets.Count + 1;
        var items = new ConcurrentDictionary<string, CleanupItem>(StringComparer.Ordinal);
        var completed = 0;
        var slowGate = new SemaphoreSlim(1, 1);

        // 快档：缓存、日志、临时目录与单文件，先出首屏结果。
        await RunTierAsync(targets.Where(target => target.Tier == ScanTier.Fast).ToArray(), FastParallelism, slowGate);

        // 慢档：DISM、大树与 Store 应用，后台补齐。
        await RunTierAsync(targets.Where(target => target.Tier == ScanTier.Slow).ToArray(), SlowParallelism, slowGate);

        progress?.Report(new(Volatile.Read(ref completed), total, "回收站"));
        items["recycle-bin"] = await ScanRecycleBinAsync(disk, cancellationToken);
        Interlocked.Increment(ref completed);

        progress?.Report(new(Volatile.Read(ref completed), total, "扫描完成"));
        stopwatch.Stop();

        var ordered = targets
            .Select(target => items.TryGetValue(target.Id, out var item) ? item : null)
            .Where(item => item is not null)
            .Select(item => item!)
            .Append(items["recycle-bin"])
            .ToArray();
        return new(disk, ordered, stopwatch.Elapsed);

        async Task RunTierAsync(
            IReadOnlyList<CleanupTargetDefinition> tier,
            int parallelism,
            SemaphoreSlim gate)
        {
            if (tier.Count == 0)
            {
                return;
            }

            await Parallel.ForEachAsync(
                tier,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = parallelism,
                    CancellationToken = cancellationToken
                },
                async (target, token) =>
                {
                    // 幽灵行跟随当前目标：先报"正在分析"（带真实路径），完成后带最终大小再报一次。
                    progress?.Report(new(
                        Volatile.Read(ref completed), total, target.DisplayName, target.Tier, null, target.ScanTarget));

                    // DISM / vssadmin 子进程必须串行，同时运行会互相锁住。
                    var serialized = target.ScanKind is ScanKind.DismAnalyze or ScanKind.VssQuery;
                    if (serialized)
                    {
                        await gate.WaitAsync(token);
                    }

                    CleanupItem item;
                    try
                    {
                        item = await ProbeAsync(target, token);
                    }
                    finally
                    {
                        if (serialized)
                        {
                            gate.Release();
                        }
                    }

                    items[target.Id] = item;
                    var done = Interlocked.Increment(ref completed);
                    progress?.Report(new(done, total, target.DisplayName, target.Tier, item));
                });
        }
    }

    /// <summary>需要拉起外部命令的分析项：DISM 组件存储与 vssadmin 卷影副本。</summary>
    private static bool RequiresExternalAnalysis(CleanupTargetDefinition target) =>
        target.ScanKind is ScanKind.DismAnalyze or ScanKind.VssQuery;

    private async Task<CleanupItem> ProbeAsync(CleanupTargetDefinition target, CancellationToken cancellationToken)
    {
        var probe = _probes.Resolve(target);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(target.EffectiveScanTimeout);

        try
        {
            var result = await probe.ProbeAsync(target, timeoutSource.Token);
            return target.ToItem(result.Bytes, result.FileCount, result.LastWriteTimeUtc, result.Note);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return target.ToItem(0, 0, null, "分析超时");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return target.ToItem(0, 0, null, $"分析失败：{exception.Message}");
        }
    }

    private async Task<CleanupItem> ScanRecycleBinAsync(DiskSnapshot disk, CancellationToken cancellationToken)
    {
        var size = _platform.IsWindows
            ? await _recycleBinInfoProvider.GetInfoAsync($"{disk.DriveName}\\", cancellationToken)
            : default;
        return new(
            "recycle-bin",
            "回收站",
            $"{disk.DriveName}\\$Recycle.Bin",
            CleanupCategory.RecycleBin,
            CleanupRisk.Medium,
            size.Bytes,
            size.FileCount,
            "清空后文件无法从回收站恢复，执行前必须单独确认。",
            "recycle-bin",
            CleanerKind: CleanerKind.RecycleBin,
            Paths: [$"{disk.DriveName}\\$Recycle.Bin"],
            Icon: "i-trash",
            Accent: "#64748b");
    }
}
