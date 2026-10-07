using System.Windows.Input;
using Avalonia.Media;
using ClearC.Core.Formatting;
using ClearC.Core.Models;
using ClearC.Desktop.Infrastructure.Logging;
using ClearC.Desktop.Infrastructure.Windows;
using ClearC.Desktop.Themes;
using ReactiveUI;

namespace ClearC.Desktop.ViewModels;

public sealed class CleanupItemViewModel : ReactiveObject
{
    private readonly IThemePalette _palette;
    private readonly bool _isElevated;
    private readonly ILocationOpener? _locationOpener;
    private readonly ITextClipboard? _clipboard;
    private bool _isSelected;
    private bool _isExpanded;
    private bool _canSelect;
    private bool _isCurrent;
    private string? _deniedReason;
    private string? _locationHint;
    private CleanupItemResult? _result;

    public CleanupItemViewModel(
        CleanupItem model,
        IThemePalette? palette = null,
        bool isElevated = true,
        ILocationOpener? locationOpener = null,
        ITextClipboard? clipboard = null)
    {
        Model = model;
        _palette = palette ?? ThemePalette.Instance;
        _isElevated = isElevated;
        _locationOpener = locationOpener;
        _clipboard = clipboard;

        var accent = ParseAccent(model.Accent);
        AccentBrush = new SolidColorBrush(accent);
        TileBackground = new SolidColorBrush(accent, 0x1f / 255d);
        TileBorder = new SolidColorBrush(accent, 0x55 / 255d);
        OpenLocationCommand = ReactiveCommand.Create(OpenLocation);
        CopyPathCommand = ReactiveCommand.CreateFromTask(CopyPathAsync);
        ToggleSelectionCommand = ReactiveCommand.Create(ToggleSelection);
        ToggleExpandedCommand = ReactiveCommand.Create(() => IsExpanded = !IsExpanded);
    }

    public event EventHandler? SelectionChanged;

    public CleanupItem Model { get; }
    public string Id => Model.Id;
    public string DisplayName => Model.DisplayName;
    public string Location => Model.Location;
    public CleanupCategory Category => Model.Category;
    public CleanupDisplayGroup DisplayGroup => Model.Category.ToDisplayGroup();
    public string Description => Model.Description;
    public string IconKey => Model.Icon;

    public IBrush AccentBrush { get; }
    public IBrush TileBackground { get; }
    public IBrush TileBorder { get; }
    public IBrush RiskForeground => _palette.RiskForeground(Model.Risk);
    public IBrush RiskBackground => _palette.RiskBackground(Model.Risk);

    public string SizeText => ByteSizeFormatter.Format(Model.SizeBytes);
    public string FileCountText => $"{Model.FileCount:N0} 个文件";
    public string RelativeTimeText => RelativeTimeFormatter.Format(Model.LastWriteTimeUtc);
    public string MetaText => $"{FileCountText} · {RelativeTimeText}";
    public string RiskText => Model.Risk switch { CleanupRisk.Low => "低风险", CleanupRisk.Medium => "中风险", _ => "高风险" };

    /// <summary>详情里的路径列表（逐行渲染，个人目录前缀替换为环境变量占位符）。</summary>
    public IReadOnlyList<string> PathLines => Model.CleanRoots.Select(PathRedactor.Redact).ToArray();

    public string PathSourceText => string.IsNullOrWhiteSpace(Model.PathSource) ? string.Empty : $"来源 {Model.PathSource}";
    public bool HasPathSource => !string.IsNullOrWhiteSpace(Model.PathSource);
    public string ScanNoteText => Model.ScanNote ?? string.Empty;
    public bool HasScanNote => !string.IsNullOrWhiteSpace(Model.ScanNote);

    /// <summary>在资源管理器中定位第一个清理路径；失败原因显示在详情里。</summary>
    public ICommand OpenLocationCommand { get; }

    /// <summary>复制完整路径到剪贴板（右键菜单）。</summary>
    public ICommand CopyPathCommand { get; }

    /// <summary>勾选 / 取消勾选（右键菜单）。</summary>
    public ICommand ToggleSelectionCommand { get; }

    /// <summary>展开 / 收起详情（右键菜单）。</summary>
    public ICommand ToggleExpandedCommand { get; }

    /// <summary>右键菜单里勾选项的文案随当前状态切换。</summary>
    public string ContextToggleText => IsSelected ? "取消勾选" : "勾选此项";

    /// <summary>右键菜单里详情项的文案随展开状态切换。</summary>
    public string ContextExpandText => IsExpanded ? "收起详情" : "展开详情";

    /// <summary>所在盘符（如 "C:"），结果列表按盘符分区。</summary>
    public string DriveName => Model.DriveName;

    public string LocationHint
    {
        get => _locationHint ?? string.Empty;
        private set
        {
            if (_locationHint == value)
            {
                return;
            }

            _locationHint = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(HasLocationHint));
        }
    }

    public bool HasLocationHint => !string.IsNullOrEmpty(_locationHint);

    public string Recommendation => Model.CleanerKey == "codex-conversations"
        ? "默认不选择。请先关闭 Codex；配置、登录信息、技能、插件、数据库和诊断日志不会删除。"
        : !Model.CanClean
        ? "由系统或应用管理，ClearC 不执行删除"
        : NeedsElevation
        ? "需要管理员权限，请先用「以管理员重启」提权后再清理"
        : Model.Risk == CleanupRisk.Low
            ? "可安全清理，缓存会在需要时自动重建"
            : "清理后不可恢复或需要重新下载，请确认影响";
    public string? SelectionToolTip => Model.CleanerKey == "codex-conversations"
        ? "默认不选择。ClearC 仅在本地删除 Codex 活动与归档会话文件，不连接 Codex；检测到 Codex 正在运行时会跳过。"
        : NeedsElevation
        ? "需要管理员权限，未提权时不可选择。"
        : null;

    /// <summary>未提权时该项不可清理（<see cref="CleanupItem.RequiresElevation"/> 为真）。</summary>
    public bool NeedsElevation => Model.RequiresElevation && !_isElevated;
    public bool IsReadOnly => !Model.CanClean || NeedsElevation;
    public string ReadOnlyText => NeedsElevation ? "需管理员" : Model.CanClean ? string.Empty : "不可清理";
    public bool HasReadOnlyTag => IsReadOnly;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!CanSelect && value)
            {
                return;
            }

            if (_isSelected == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _isSelected, value);
            this.RaisePropertyChanged(nameof(ContextToggleText));
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                this.RaiseAndSetIfChanged(ref _isExpanded, value);
                this.RaisePropertyChanged(nameof(ChevronRotation));
                this.RaisePropertyChanged(nameof(ContextExpandText));
            }
        }
    }

    /// <summary>展开时 chevron 旋转 180°（原型 <c>.row.expanded .chev</c>）。</summary>
    public double ChevronRotation => IsExpanded ? 180 : 0;

    public bool CanSelect
    {
        get => _canSelect && Model.CanClean && !NeedsElevation && _deniedReason is null && _result is null;
        set
        {
            if (_canSelect != value)
            {
                _canSelect = value;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(CanSelect));
            }
        }
    }

    public bool IsCurrent
    {
        get => _isCurrent;
        set => this.RaiseAndSetIfChanged(ref _isCurrent, value);
    }

    /// <summary>安全策略拒绝该行时给出原因；非空表示确认页要标红并禁用「开始清理」。</summary>
    public string? DeniedReason
    {
        get => _deniedReason;
        set
        {
            if (_deniedReason == value)
            {
                return;
            }

            _deniedReason = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(IsDenied));
            this.RaisePropertyChanged(nameof(CanSelect));
        }
    }

    public bool IsDenied => _deniedReason is not null;

    public bool HasResult => _result is not null;
    public double RowOpacity => HasResult ? 0.45 : 1;
    public string ResultText => _result?.Outcome switch
    {
        CleanupOutcome.Completed => "✓ 已清理",
        CleanupOutcome.Skipped => "已跳过",
        CleanupOutcome.Failed => "清理失败",
        CleanupOutcome.Cancelled => "已取消",
        _ => string.Empty
    };
    public string FreedText => _result is { FreedBytes: > 0 } result
        ? result.IsEstimated
            ? $"释放 ~{ByteSizeFormatter.Format(result.FreedBytes)}（估算）"
            : $"释放 {ByteSizeFormatter.Format(result.FreedBytes)}"
        : string.Empty;
    public IBrush ResultBrush => _result?.Outcome switch
    {
        CleanupOutcome.Completed => _palette.Brush("ClearCGreen"),
        CleanupOutcome.Skipped => _palette.Brush("ClearCAmber"),
        CleanupOutcome.Failed => _palette.Brush("ClearCRed"),
        _ => _palette.Brush("ClearCText3")
    };

    public void SetInitialSelection(bool selected)
    {
        _isSelected = selected;
        this.RaisePropertyChanged(nameof(IsSelected));
    }

    public void ApplyResult(CleanupItemResult result)
    {
        _result = result;
        _isCurrent = false;
        _isSelected = false;
        this.RaisePropertyChanged(nameof(IsCurrent));
        this.RaisePropertyChanged(nameof(IsSelected));
        this.RaisePropertyChanged(nameof(CanSelect));
        this.RaisePropertyChanged(nameof(HasResult));
        this.RaisePropertyChanged(nameof(RowOpacity));
        this.RaisePropertyChanged(nameof(ResultText));
        this.RaisePropertyChanged(nameof(FreedText));
        this.RaisePropertyChanged(nameof(ResultBrush));
    }

    private void OpenLocation()
    {
        var path = Model.CleanRoots.FirstOrDefault();
        LocationHint = _locationOpener is null
            ? "当前环境无法打开资源管理器"
            : _locationOpener.TryOpen(path ?? string.Empty) ?? string.Empty;
    }

    private async Task CopyPathAsync()
    {
        if (_clipboard is null)
        {
            LocationHint = "当前环境没有可用剪贴板";
            return;
        }

        await _clipboard.CopyAsync(Model.Location);
        LocationHint = "路径已复制到剪贴板";
    }

    private void ToggleSelection() => IsSelected = !IsSelected;

    private static Color ParseAccent(string? accent) =>
        Color.TryParse(accent, out var parsed) ? parsed : Color.Parse("#2f6bff");
}
