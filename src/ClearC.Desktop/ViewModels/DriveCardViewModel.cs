using ClearC.Core.Formatting;
using ClearC.Core.Models;
using ReactiveUI;

namespace ClearC.Desktop.ViewModels;

/// <summary>
/// 盘符选择条上的一张盘符卡（原型 <c>.dcard</c>）：复选框 + 类型徽标 + 容量条 + 扫描状态。
/// 复选框勾选 = 该盘符纳入扫描范围。
/// </summary>
public sealed class DriveCardViewModel : ReactiveObject
{
    private readonly Action<DriveCardViewModel, bool> _onCheckedChanged;
    private DiskSnapshot _snapshot;
    private bool _isChecked;
    private bool _isLocked;
    private string _scanSummary = string.Empty;
    private bool _isScanPending;

    public DriveCardViewModel(
        DiskSnapshot snapshot,
        bool isSystemDrive,
        bool isChecked,
        Action<DriveCardViewModel, bool> onCheckedChanged)
    {
        _snapshot = snapshot;
        IsSystemDrive = isSystemDrive;
        _isChecked = isChecked;
        _onCheckedChanged = onCheckedChanged;
    }

    public DiskSnapshot Snapshot => _snapshot;

    /// <summary>盘符（如 "C:"）。</summary>
    public string DriveName => _snapshot.DriveName;

    /// <summary>盘符字母（如 "C"），盘符卡左端的醒目字母。</summary>
    public string Letter => _snapshot.DriveName.TrimEnd('\\', '/');

    public bool IsSystemDrive { get; }
    public string TypeText => IsSystemDrive ? "系统盘" : "数据盘";

    public string VolumeLabel => string.IsNullOrWhiteSpace(_snapshot.VolumeLabel)
        ? string.Empty
        : _snapshot.VolumeLabel!;
    public bool HasVolumeLabel => VolumeLabel.Length > 0;

    public string FormatText => $"{ByteSizeFormatter.Format(_snapshot.TotalBytes)} · {_snapshot.DriveFormat}";

    public double UsedRatio => _snapshot.UsedRatio;
    public string UsedPercentText => $"{Math.Round(_snapshot.UsedRatio * 100):0}%";
    public bool IsNearlyFull => _snapshot.UsedRatio >= 0.9;

    public string UsageText =>
        $"已用 {ByteSizeFormatter.Format(_snapshot.UsedBytes)} / {ByteSizeFormatter.Format(_snapshot.TotalBytes)} · {UsedPercentText}";

    /// <summary>容量条填充宽度（轨道 150px，原型 <c>.dc-bar</c>）。</summary>
    public double UsageBarWidth => Math.Clamp(UsedRatio, 0, 1) * 150;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (this.RaiseAndSetIfChanged(ref _isChecked, value))
            {
                _onCheckedChanged(this, value);
            }
        }
    }

    /// <summary>扫描 / 清理进行中锁定，不可修改扫描范围。</summary>
    public bool IsLocked
    {
        get => _isLocked;
        set => this.RaiseAndSetIfChanged(ref _isLocked, value);
    }

    public string LockedToolTip => "扫描 / 清理过程中不可修改范围";

    /// <summary>卡尾的扫描状态：等待扫描 / 正在扫描 … / 可清理汇总。</summary>
    public string ScanSummary
    {
        get => _scanSummary;
            set
            {
                if (_scanSummary == value)
                {
                    return;
                }

                this.RaiseAndSetIfChanged(ref _scanSummary, value);
                this.RaisePropertyChanged(nameof(HasScanSummary));
            }
    }

    public bool HasScanSummary => ScanSummary.Length > 0;

    /// <summary>true = 等待 / 进行中的占位文案（灰色），false = 已完成的可清理汇总（强调色）。</summary>
    public bool IsScanPending
    {
        get => _isScanPending;
        set { this.RaiseAndSetIfChanged(ref _isScanPending, value); }
    }

    /// <summary>清理完成后用最新快照刷新容量条（释放量按盘符累计）。</summary>
    internal void UpdateSnapshot(DiskSnapshot snapshot)
    {
        _snapshot = snapshot;
        this.RaisePropertyChanged(nameof(Snapshot));
        this.RaisePropertyChanged(nameof(FormatText));
        this.RaisePropertyChanged(nameof(UsedRatio));
        this.RaisePropertyChanged(nameof(UsedPercentText));
        this.RaisePropertyChanged(nameof(IsNearlyFull));
        this.RaisePropertyChanged(nameof(UsageText));
        this.RaisePropertyChanged(nameof(UsageBarWidth));
    }
}
