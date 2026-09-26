using System.Globalization;
using System.Text.RegularExpressions;
using ClearC.Core.Formatting;
using ClearC.Desktop.Infrastructure.Cleanup;
using ClearC.Desktop.Infrastructure.Windows;

namespace ClearC.Desktop.Infrastructure.Scanning;

internal sealed record ScanProbeResult(
    long Bytes,
    long FileCount,
    DateTimeOffset? LastWriteTimeUtc = null,
    string? Note = null,
    bool TooLongPathFiles = false);

internal interface IScanProbe
{
    ScanKind Kind { get; }

    Task<ScanProbeResult> ProbeAsync(CleanupTargetDefinition target, CancellationToken cancellationToken);
}

internal sealed class ScanProbeRegistry
{
    private readonly IReadOnlyDictionary<ScanKind, IScanProbe> _probes;

    public ScanProbeRegistry(IEnumerable<IScanProbe> probes)
    {
        _probes = probes.ToDictionary(probe => probe.Kind);
        var missing = Enum.GetValues<ScanKind>().Where(kind => !_probes.ContainsKey(kind)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException($"缺少扫描探测器：{string.Join("、", missing)}");
        }
    }

    public IScanProbe Resolve(CleanupTargetDefinition target) =>
        _probes.TryGetValue(target.ScanKind, out var probe)
            ? probe
            : throw new InvalidOperationException($"没有匹配 {target.ScanKind} 的扫描探测器。");
}

/// <summary>目录 / 文件模式 / 单文件都由目录大小计算器完成，区别只在过滤条件。</summary>
internal sealed class DirectorySizeProbe(IDirectorySizeCalculator sizeCalculator) : IScanProbe
{
    public ScanKind Kind => ScanKind.Directory;

    public async Task<ScanProbeResult> ProbeAsync(CleanupTargetDefinition target, CancellationToken cancellationToken)
    {
        var request = new DirectorySizeRequest(
            target.Paths,
            target.MinimumAge is null ? null : DateTimeOffset.UtcNow - target.MinimumAge.Value,
            target.IncludePatterns,
            target.ExcludePaths);
        var size = await sizeCalculator.CalculateAsync(request, cancellationToken);
        var note = size.TooLongPathFiles > 0
            ? $"有 {size.TooLongPathFiles:N0} 个超长路径文件未统计"
            : target.ScanNote;
        return new(size.Bytes, size.FileCount, size.LastWriteTimeUtc, note, size.TooLongPathFiles > 0);
    }
}

internal sealed class FilePatternProbe(IDirectorySizeCalculator sizeCalculator) : IScanProbe
{
    public ScanKind Kind => ScanKind.FilePattern;

    public Task<ScanProbeResult> ProbeAsync(CleanupTargetDefinition target, CancellationToken cancellationToken) =>
        new DirectorySizeProbe(sizeCalculator).ProbeAsync(target, cancellationToken);
}

internal sealed class SingleFileProbe(IDirectorySizeCalculator sizeCalculator) : IScanProbe
{
    public ScanKind Kind => ScanKind.SingleFile;

    public Task<ScanProbeResult> ProbeAsync(CleanupTargetDefinition target, CancellationToken cancellationToken) =>
        new DirectorySizeProbe(sizeCalculator).ProbeAsync(target, cancellationToken);
}

/// <summary>
/// WinSxS 用 DISM 官方数字；dism 缺失 / 超时 / 解析失败时回退目录扫描并标注偏差。
/// 未提权时不再扫 WinSxS 目录：那是几万文件的大树，未提权大部分读不到，
/// 既慢又偏小，直接标注"需管理员"。
/// </summary>
internal sealed partial class DismProbe(
    IDirectorySizeCalculator sizeCalculator,
    IProcessRunner processRunner,
    IElevationService elevationService) : IScanProbe
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    public ScanKind Kind => ScanKind.DismAnalyze;

    public async Task<ScanProbeResult> ProbeAsync(CleanupTargetDefinition target, CancellationToken cancellationToken)
    {
        if (!elevationService.IsElevated)
        {
            return new(0, 0, null, "需管理员：WinSxS 官方数字与目录扫描都需要管理员权限");
        }

        var run = await processRunner.RunAsync(
            "dism",
            ["/Online", "/Cleanup-Image", "/AnalyzeComponentStore"],
            cancellationToken,
            Timeout);
        if (run.TimedOut)
        {
            return await FallbackAsync(target, "DISM 分析超时：当前显示目录扫描数字（含硬链接，偏大）", cancellationToken);
        }

        if (!run.Succeeded)
        {
            return await FallbackAsync(target, "DISM 分析失败：当前显示目录扫描数字（含硬链接，偏大）", cancellationToken);
        }

        var sizes = ParseSizes(run.StandardOutput);
        if (sizes.Count < 2)
        {
            return await FallbackAsync(target, "无法解析 DISM 数字：当前显示目录扫描数字（含硬链接，偏大）", cancellationToken);
        }

        // DISM 输出的顺序固定：报告大小 / 实际大小 / 与 Windows 共享 / 备份与禁用功能 / 缓存与临时数据。
        var actual = sizes[1];
        var reclaimable = sizes.Count >= 5 ? sizes[3] + sizes[4] : sizes.Count >= 4 ? sizes[3] : 0;
        var note = reclaimable > 0
            ? $"DISM 官方数字 · 可回收约 {ByteSizeFormatter.Format(reclaimable)}"
            : "DISM 官方数字";
        return new(actual, 0, null, note);
    }

    private async Task<ScanProbeResult> FallbackAsync(
        CleanupTargetDefinition target,
        string note,
        CancellationToken cancellationToken)
    {
        var measured = await sizeCalculator.CalculateAsync(
            new DirectorySizeRequest(target.Paths, null, target.IncludePatterns, target.ExcludePaths),
            cancellationToken);
        return new(measured.Bytes, measured.FileCount, measured.LastWriteTimeUtc, note, measured.TooLongPathFiles > 0);
    }

    internal static IReadOnlyList<long> ParseSizes(string output)
    {
        var sizes = new List<long>();
        foreach (Match match in SizePattern().Matches(output))
        {
            var number = match.Groups[1].Value.Replace(",", string.Empty);
            if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                continue;
            }

            var multiplier = match.Groups[2].Value.ToUpperInvariant() switch
            {
                "TB" => 1024L * 1024 * 1024 * 1024,
                "GB" => 1024L * 1024 * 1024,
                "MB" => 1024L * 1024,
                "KB" => 1024L,
                _ => 1L
            };
            sizes.Add((long)(value * multiplier));
        }

        return sizes;
    }

    [GeneratedRegex(@"([\d]+(?:[.,]\d+)?)\s*(TB|GB|MB|KB|B)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SizePattern();
}

/// <summary>卷影副本存储：只解析数字，不提供清理入口。</summary>
internal sealed partial class VssProbe(IProcessRunner processRunner, IElevationService elevationService) : IScanProbe
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    public ScanKind Kind => ScanKind.VssQuery;

    public async Task<ScanProbeResult> ProbeAsync(CleanupTargetDefinition target, CancellationToken cancellationToken)
    {
        if (!elevationService.IsElevated)
        {
            return new(0, 0, null, "需管理员查看卷影副本占用");
        }

        var run = await processRunner.RunAsync("vssadmin", ["list", "shadowstorage"], cancellationToken, Timeout);
        if (!run.Succeeded)
        {
            return new(0, 0, null, "需管理员查看卷影副本占用");
        }

        var sizes = DismProbe.ParseSizes(run.StandardOutput);
        if (sizes.Count == 0)
        {
            return new(0, 0, null, "无法解析 vssadmin 输出，请用管理员命令行查看");
        }

        var note = sizes.Count >= 2
            ? $"已用 {ByteSizeFormatter.Format(sizes[0])} · 上限 {ByteSizeFormatter.Format(sizes[1])}"
            : $"已用 {ByteSizeFormatter.Format(sizes[0])}";
        return new(sizes[0], 0, null, note);
    }
}
