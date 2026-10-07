using System.Collections.ObjectModel;
using ReactiveUI;

namespace ClearC.Desktop.ViewModels;

/// <summary>
/// 结果列表里的一个盘符分区（原型 <c>.drive-sec</c>）：
/// 分区头 = 盘符 + 类型 + 默认勾选策略徽标 + 已选小计 + 可清理汇总，内部再按展示分组。
/// </summary>
public sealed class CleanupDriveSectionViewModel : ReactiveObject
{
    private string _policyText = string.Empty;
    private bool _isPolicyAuto;
    private string _selectedText = string.Empty;
    private string _summaryText = string.Empty;

    /// <summary>缓存键：同盘符的分区实例跨刷新复用，保住组内展开状态。</summary>
    public CleanupDriveSectionViewModel(
        string driveName,
        string typeText,
        string volumeLabel)
    {
        DriveName = driveName;
        TypeText = typeText;
        VolumeLabel = volumeLabel;
    }

    /// <summary>盘符（如 "C:"），分区头左端的字母徽标。</summary>
    public string DriveName { get; }

    public string TypeText { get; }

    public string DriveTitle => $"{DriveName} {TypeText}";

    public string VolumeLabel { get; }

    public bool HasVolumeLabel => VolumeLabel.Length > 0;

    /// <summary>默认勾选策略徽标：低风险已默认勾选（绿）/ 默认未勾选 · 请自行决策（琥珀）。</summary>
    public string PolicyText
    {
        get => _policyText;
        internal set { this.RaiseAndSetIfChanged(ref _policyText, value); }
    }

    public bool IsPolicyAuto
    {
        get => _isPolicyAuto;
        internal set { this.RaiseAndSetIfChanged(ref _isPolicyAuto, value); }
    }

    /// <summary>该盘已勾选小计（如 "已选 9 项 · 9.57 GB"），未选中任何项时为空。</summary>
    public string SelectedText
    {
        get => _selectedText;
        internal set
        {
            if (_selectedText == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedText, value);
            this.RaisePropertyChanged(nameof(HasSelectedText));
        }
    }

    public bool HasSelectedText => SelectedText.Length > 0;

    /// <summary>该盘可清理汇总 / 扫描状态（如 "可清理 13 项 · 29.53 GB"）。</summary>
    public string SummaryText
    {
        get => _summaryText;
        internal set
        {
            if (_summaryText == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _summaryText, value);
            this.RaisePropertyChanged(nameof(HasSummaryText));
        }
    }

    public bool HasSummaryText => SummaryText.Length > 0;

    public ObservableCollection<CleanupGroupViewModel> Groups { get; } = [];
}
