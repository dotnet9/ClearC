namespace ClearC.Core.Models;

/// <summary>
/// 扫描进度事件。<see cref="Item"/> 为 <c>null</c> 表示该目标正在分析（幽灵行），
/// 非 <c>null</c> 表示该目标分析完成并携带最终大小。
/// </summary>
public sealed record ScanProgress(
    int Completed,
    int Total,
    string CurrentTarget,
    ScanTier Tier = ScanTier.Fast,
    CleanupItem? Item = null,
    string? TargetPath = null)
{
    public double Ratio => Total <= 0 ? 0 : Math.Clamp((double)Completed / Total, 0, 1);

    public bool IsItemCompleted => Item is not null;

    /// <summary>幽灵行文案：优先显示正在分析的真实路径，取不到时退回目标名。</summary>
    public string GhostText => $"▍ 正在扫描 {TargetPath ?? CurrentTarget} …";

    /// <summary>原型里的进度文案：<c>NN/NN · 当前目标</c>，慢档追加“（慢速目标）”。</summary>
    public string DisplayText
    {
        get
        {
            var index = Math.Clamp(IsItemCompleted ? Completed : Completed + 1, 0, Math.Max(Total, 1));
            var suffix = Tier == ScanTier.Slow ? "（慢速目标）" : string.Empty;
            return $"{index:00}/{Total:00} · {CurrentTarget}{suffix}";
        }
    }
}
