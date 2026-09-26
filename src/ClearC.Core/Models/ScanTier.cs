namespace ClearC.Core.Models;

/// <summary>
/// 扫描分档：快档先出结果，慢档（DISM、大树、Store 应用）后台补齐。
/// </summary>
public enum ScanTier
{
    Fast,
    Slow
}
