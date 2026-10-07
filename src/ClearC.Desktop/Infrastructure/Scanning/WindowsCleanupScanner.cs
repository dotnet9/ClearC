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
        bool skipSystemAnalysis = false,
        IReadOnlyList<string>? driveScope = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var disk = _diskInfoProvider.GetSystemDrive();
        var scope = driveScope is not { Count: > 0 }
            ? [disk.DriveName]
            : driveScope.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var includeSystem = scope.Contains(disk.DriveName, StringComparer.OrdinalIgnoreCase);
        var resolved = _platform.IsWindows
            ? await _catalog.ResolveTargetsAsync(scope, cancellationToken)
            : [];
        // 快速模式：不拉起 DISM / vssadmin 子进程，别为两个分析项让整次扫描多等几分钟。
        var analysisTargets = skipSystemAnalysis
            ? resolved.Where(target => !RequiresExternalAnalysis(target)).ToArray()
            : resolved;
        // 回收站不走路由探测（$Recycle.Bin 枚举受限、数字偏小），统一用 Shell API 查询（§3.1）。
        var recycleRows = analysisTargets.Where(target => target.CleanerKind == CleanerKind.RecycleBin).ToArray();
        var targets = analysisTargets.Where(target => target.CleanerKind != CleanerKind.RecycleBin).ToArray();
        var total = targets.Length + (includeSystem ? 1 : 0) + recycleRows.Count(row => !row.Id.Equals("recycle-bin", StringComparison.Ordinal));
        var items = new ConcurrentDictionary<string, CleanupItem>(StringComparer.Ordinal);
        var completed = 0;
        var slowGate = new SemaphoreSlim(1, 1);

        // 快档：缓存、日志、临时目录与单文件，先出首屏结果。
        await RunTierAsync(targets.Where(target => target.Tier == ScanTier.Fast).ToArray(), FastParallelism, slowGate);

        // 慢档：DISM、大树与 Store 应用，后台补齐。
        await RunTierAsync(targets.Where(target => target.Tier == ScanTier.Slow).ToArray(), SlowParallelism, slowGate);

        var ordered = targets
            .Select(target => items.TryGetValue(target.Id, out var item) ? item : null)
            .Where(item => item is not null)
            .Select(item => item!)
            .ToList();

        // 回收站按扫描范围顺序输出：系统盘在前，其他盘符随后，逐盘用 Shell 数字产出条目。
        if (includeSystem)
        {
            progress?.Report(new(Volatile.Read(ref completed), total, "回收站"));
            items["recycle-bin"] = await ScanRecycleBinAsync(
                disk, "recycle-bin", "回收站", $"{disk.DriveName}\\$Recycle.Bin", cancellationToken);
            ordered.Add(items["recycle-bin"]);
            Interlocked.Increment(ref completed);
        }

        foreach (var row in recycleRows.Where(row => !row.Id.Equals("recycle-bin", StringComparison.Ordinal)))
        {
            var driveName = Path.GetPathRoot(row.Paths.FirstOrDefault() ?? row.Location)?.TrimEnd('\\', '/') ?? string.Empty;
            var snapshot = new DiskSnapshot(driveName, string.Empty, 0, 0);
            progress?.Report(new(Volatile.Read(ref completed), total, row.DisplayName, ScanTier.Fast, null, row.ScanTarget));
            items[row.Id] = await ScanRecycleBinAsync(snapshot, row.Id, row.DisplayName, row.Location, cancellationToken);
            ordered.Add(items[row.Id]);
            Interlocked.Increment(ref completed);
        }

        progress?.Report(new(Volatile.Read(ref completed), total, "扫描完成"));
        stopwatch.Stop();

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

    private async Task<CleanupItem> ScanRecycleBinAsync(
        DiskSnapshot disk,
        string id,
        string displayName,
        string location,
        CancellationToken cancellationToken)
    {
        var size = _platform.IsWindows
            ? await _recycleBinInfoProvider.GetInfoAsync($"{disk.DriveName}\\", cancellationToken)
            : default;
        return new(
            id,
            displayName,
            location,
            CleanupCategory.RecycleBin,
            CleanupRisk.Medium,
            size.Bytes,
            size.FileCount,
            "清空后文件无法从回收站恢复，执行前必须单独确认。",
            id,
            CleanerKind: CleanerKind.RecycleBin,
            Paths: [location],
            Icon: "i-trash",
            Accent: "#64748b");
    }
}
