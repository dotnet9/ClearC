using ClearC.Core.Models;

namespace ClearC.Core.Services;

public interface ICleanupScanner
{
    /// <summary>
    /// 扫描系统盘。
    /// <paramref name="skipSystemAnalysis"/> 为 <c>true</c> 时跳过需要外部命令的系统分析项
    /// （DISM 组件存储、vssadmin 卷影副本），这是默认的"快速模式"：这两项各自要拉起子进程，
    /// 未提权时还拿不到数字，只有用户主动要看时才值得等待。
    /// </summary>
    Task<ScanResult> ScanAsync(
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default,
        bool skipSystemAnalysis = false);
}
