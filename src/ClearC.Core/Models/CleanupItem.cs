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

    public bool IsRecommended => CanClean && SizeBytes > 0 && Risk == CleanupRisk.Low;
}
