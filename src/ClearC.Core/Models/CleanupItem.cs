namespace ClearC.Core.Models;

public sealed record CleanupItem(
    string Id,
    string DisplayName,
    string Location,
    CleanupCategory Category,
    CleanupRisk Risk,
    long SizeBytes,
    long FileCount,
    string Description,
    string? CleanerKey,
    bool RequiresElevation = false,
    bool IsProtected = false,
    CleanerKind CleanerKind = CleanerKind.None,
    IReadOnlyList<string>? Paths = null,
    DateTimeOffset? LastWriteTimeUtc = null,
    string? PathSource = null,
    string? ScanNote = null,
    string Icon = "i-file",
    string Accent = "#2f6bff")
{
    /// <summary>清理根目录；为空时回退到 <see cref="Location"/>。</summary>
    public IReadOnlyList<string> CleanRoots => Paths is { Count: > 0 } ? Paths : [Location];

    /// <summary>不可清理（仅分析）时为 true；与 <see cref="CanClean"/> 互补。</summary>
    public bool IsAnalyzeOnly => !CanClean;

    public bool CanClean => CleanerKind != CleanerKind.None && !IsProtected;

    /// <summary>该项所在的盘符（如 "C:"）；根目录解析不出时回退系统盘。</summary>
    public string DriveName => Path.GetPathRoot(CleanRoots[0]) is { } root
        ? root.TrimEnd('\\', '/')
        : SystemDriveRoot ?? string.Empty;

    /// <summary>是否位于系统盘（多盘扫描时，非系统盘结果一律交给用户决策）。</summary>
    public bool IsOnSystemDrive => SystemDriveRoot is null
        || string.Equals(DriveName, SystemDriveRoot, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 默认勾选策略：仅系统盘上的低风险可清理项（影响不大）默认勾选；
    /// 中/高风险项与其他盘符的全部结果默认不勾选，由用户决策。
    /// </summary>
    public bool IsRecommended => CanClean && SizeBytes > 0 && Risk == CleanupRisk.Low && IsOnSystemDrive;

    private static readonly string? SystemDriveRoot = OperatingSystem.IsWindows()
        ? Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\', '/')
        : null;
}
