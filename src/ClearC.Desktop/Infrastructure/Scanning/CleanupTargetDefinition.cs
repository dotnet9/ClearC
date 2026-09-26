using ClearC.Core.Models;

namespace ClearC.Desktop.Infrastructure.Scanning;

/// <summary>决定用哪个扫描探测器。</summary>
internal enum ScanKind
{
    Directory,
    FilePattern,
    SingleFile,
    DismAnalyze,
    VssQuery
}

/// <summary>官方清理命令。</summary>
internal sealed record CleanerCommand(
    string FileName,
    IReadOnlyList<string> Arguments,
    string SuccessMessage,
    TimeSpan? Timeout = null);

/// <summary>
/// 一个清理目标的完整定义。所有路径类字段都必须是绝对路径。
/// </summary>
internal sealed record CleanupTargetDefinition(
    string Id,
    string DisplayName,
    CleanupCategory Category,
    CleanupRisk Risk,
    string Description,
    IReadOnlyList<string> Paths,
    CleanerKind CleanerKind = CleanerKind.None,
    IReadOnlyList<string>? AllowedRoots = null,
    IReadOnlyList<string>? IncludePatterns = null,
    IReadOnlyList<string>? ExcludePaths = null,
    CleanerCommand? Command = null,
    ScanKind ScanKind = ScanKind.Directory,
    ScanTier Tier = ScanTier.Fast,
    TimeSpan? MinimumAge = null,
    TimeSpan? ScanTimeout = null,
    bool RequiresElevation = false,
    bool IsProtected = false,
    bool CheckLoadedModules = false,
    string Icon = "i-file",
    string Accent = "#2f6bff",
    string? LocationOverride = null,
    string? PathSource = null,
    string? ScanNote = null,
    string? ScanLabel = null)
{
    /// <summary>执行白名单：清理时 <see cref="CleanupItem.CleanRoots"/> 必须落在这些父目录内。</summary>
    public IReadOnlyList<string> EffectiveAllowedRoots => AllowedRoots is { Count: > 0 } ? AllowedRoots : Paths;

    /// <summary>原型里显示的路径：单路径直接显示，多路径显示“首路径 等 N 个目录”。</summary>
    public string Location => LocationOverride ?? Paths.Count switch
    {
        0 => string.Empty,
        1 => Paths[0],
        _ => $"{Paths[0]} 等 {Paths.Count} 个目录"
    };

    /// <summary>幽灵行文案：命令类目标（vssadmin / DISM）显示可读标签，其余显示真实路径。</summary>
    public string ScanTarget => ScanLabel ?? Location;

    public bool IsAnalyzeOnly => CleanerKind == CleanerKind.None;

    /// <summary>每目标扫描超时：普通 60s、DISM/vssadmin 90s（要拉子进程）、大树 120s。</summary>
    public TimeSpan EffectiveScanTimeout => ScanTimeout ?? ScanKind switch
    {
        ScanKind.DismAnalyze or ScanKind.VssQuery => TimeSpan.FromSeconds(90),
        _ => TimeSpan.FromSeconds(60)
    };

    public CleanupItem ToItem(
        long sizeBytes,
        long fileCount,
        DateTimeOffset? lastWriteTimeUtc = null,
        string? scanNote = null) => new(
        Id,
        DisplayName,
        Location,
        Category,
        Risk,
        sizeBytes,
        fileCount,
        Description,
        IsAnalyzeOnly ? null : Id,
        RequiresElevation,
        IsProtected,
        CleanerKind,
        Paths,
        lastWriteTimeUtc,
        PathSource,
        scanNote ?? ScanNote,
        Icon,
        Accent);
}
