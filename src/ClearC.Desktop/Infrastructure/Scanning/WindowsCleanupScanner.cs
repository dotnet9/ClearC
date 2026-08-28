using System.Diagnostics;
using ClearC.Core.Models;
using ClearC.Core.Services;

namespace ClearC.Desktop.Infrastructure.Scanning;

public sealed class WindowsCleanupScanner : ICleanupScanner
{
    private readonly ICleanupTargetCatalog _catalog;
    private readonly IDirectorySizeCalculator _sizeCalculator;
    private readonly IDiskInfoProvider _diskInfoProvider;
    private readonly IRecycleBinInfoProvider _recycleBinInfoProvider;

    public WindowsCleanupScanner()
        : this(
            new WindowsCleanupTargetCatalog(),
            new DirectorySizeCalculator(),
            new WindowsDiskInfoProvider(),
            new WindowsRecycleBinInfoProvider())
    {
    }

    internal WindowsCleanupScanner(
        ICleanupTargetCatalog catalog,
        IDirectorySizeCalculator sizeCalculator,
        IDiskInfoProvider diskInfoProvider,
        IRecycleBinInfoProvider recycleBinInfoProvider)
    {
        _catalog = catalog;
        _sizeCalculator = sizeCalculator;
        _diskInfoProvider = diskInfoProvider;
        _recycleBinInfoProvider = recycleBinInfoProvider;
    }

    public async Task<ScanResult> ScanAsync(
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var disk = _diskInfoProvider.GetSystemDrive();
        var targets = _catalog.GetTargets();
        var items = new List<CleanupItem>(targets.Count + 1);

        for (var index = 0; index < targets.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = targets[index];
            progress?.Report(new(index, targets.Count + 1, target.DisplayName));
            DateTimeOffset? cutoff = target.MinimumAge is null
                ? null
                : DateTimeOffset.UtcNow - target.MinimumAge.Value;
            var size = await _sizeCalculator.CalculateAsync(target.Paths, cutoff, cancellationToken);
            items.Add(ToItem(target, size));
        }

        progress?.Report(new(targets.Count, targets.Count + 1, "回收站"));
        var recycle = _recycleBinInfoProvider.GetInfo($"{disk.DriveName}\\");
        items.Add(new(
            "recycle-bin",
            "回收站",
            $"{disk.DriveName}\\$Recycle.Bin",
            CleanupCategory.RecycleBin,
            CleanupRisk.Medium,
            recycle.Bytes,
            recycle.FileCount,
            "清空后文件无法从回收站恢复，执行前必须单独确认。",
            "recycle-bin"));

        progress?.Report(new(targets.Count + 1, targets.Count + 1, "扫描完成"));
        stopwatch.Stop();
        return new(disk, items, stopwatch.Elapsed);
    }

    private static CleanupItem ToItem(CleanupTargetDefinition target, DirectorySize size) => new(
        target.Id,
        target.DisplayName,
        target.Location,
        target.Category,
        target.Risk,
        size.Bytes,
        size.FileCount,
        target.Description,
        target.CleanerKey,
        target.RequiresElevation,
        target.IsProtected);
}
