using Avalonia.Media;
using ClearC.Core.Formatting;
using ClearC.Core.Models;
using ReactiveUI;

namespace ClearC.Desktop.ViewModels;

public sealed class CleanupItemViewModel : ReactiveObject
{
    private bool _isSelected;
    private bool _isExpanded;
    private bool _canSelect;
    private bool _isCurrent;
    private CleanupItemResult? _result;

    public CleanupItemViewModel(CleanupItem model)
    {
        Model = model;
        AccentBrush = new SolidColorBrush(model.Category switch
        {
            CleanupCategory.PackageCache => Color.Parse("#22D3EE"),
            CleanupCategory.TemporaryFiles => Color.Parse("#38BDF8"),
            CleanupCategory.BrowserCache => Color.Parse("#818CF8"),
            CleanupCategory.RecycleBin => Color.Parse("#94A3B8"),
            CleanupCategory.ApplicationData => Color.Parse("#34D399"),
            _ => Color.Parse("#A78BFA")
        });
        RiskBrush = new SolidColorBrush(model.Risk switch
        {
            CleanupRisk.Low => Color.Parse("#34D399"),
            CleanupRisk.Medium => Color.Parse("#FBBF24"),
            _ => Color.Parse("#F87171")
        });
    }

    public event EventHandler? SelectionChanged;

    public CleanupItem Model { get; }
    public string Id => Model.Id;
    public string DisplayName => Model.DisplayName;
    public string Location => Model.Location;
    public CleanupCategory Category => Model.Category;
    public string Description => Model.Description;
    public string SizeText => ByteSizeFormatter.Format(Model.SizeBytes);
    public string FileCountText => $"{Model.FileCount:N0} 个文件";
    public string RiskText => Model.Risk switch { CleanupRisk.Low => "低风险", CleanupRisk.Medium => "中风险", _ => "高风险" };
    public string Recommendation => Model.CleanerKey == "codex-conversations"
        ? "默认不选择。请先关闭 Codex；配置、登录信息、技能、插件、数据库和诊断日志不会删除。"
        : !Model.CanClean
        ? "由系统或应用管理，ClearC 不执行删除"
        : Model.Risk == CleanupRisk.Low
            ? "可安全清理，缓存会在需要时自动重建"
            : "清理后不可恢复或需要重新下载，请确认影响";
    public string? SelectionToolTip => Model.CleanerKey == "codex-conversations"
        ? "默认不选择。ClearC 仅在本地删除 Codex 活动与归档会话文件，不连接 Codex；检测到 Codex 正在运行时会跳过。"
        : null;
    public string IconGlyph => Model.Category switch
    {
        CleanupCategory.PackageCache => "▣",
        CleanupCategory.TemporaryFiles => "□",
        CleanupCategory.BrowserCache => "◎",
        CleanupCategory.RecycleBin => "⌫",
        CleanupCategory.ApplicationData => "◇",
        _ => "◈"
    };
    public IBrush AccentBrush { get; }
    public IBrush RiskBrush { get; }
    public bool IsReadOnly => !Model.CanClean;
    public string ReadOnlyText => IsReadOnly ? "仅分析" : string.Empty;

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
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => this.RaiseAndSetIfChanged(ref _isExpanded, value);
    }

    public bool CanSelect
    {
        get => _canSelect && Model.CanClean && _result is null;
        set
        {
            if (_canSelect != value)
            {
                this.RaiseAndSetIfChanged(ref _canSelect, value);
                this.RaisePropertyChanged(nameof(CanSelect));
            }
        }
    }

    public bool IsCurrent
    {
        get => _isCurrent;
        set => this.RaiseAndSetIfChanged(ref _isCurrent, value);
    }

    public bool HasResult => _result is not null;
    public double RowOpacity => HasResult ? 0.58 : 1;
    public string ResultText => _result?.Outcome switch
    {
        CleanupOutcome.Completed => "✓ 已清理",
        CleanupOutcome.Skipped => "已跳过",
        CleanupOutcome.Failed => "清理失败",
        CleanupOutcome.Cancelled => "已取消",
        _ => string.Empty
    };
    public string FreedText => _result is { FreedBytes: > 0 } ? $"释放 {ByteSizeFormatter.Format(_result.FreedBytes)}" : string.Empty;
    public IBrush ResultBrush => _result?.Outcome switch
    {
        CleanupOutcome.Completed => new SolidColorBrush(Color.Parse("#34D399")),
        CleanupOutcome.Skipped => new SolidColorBrush(Color.Parse("#FBBF24")),
        CleanupOutcome.Failed => new SolidColorBrush(Color.Parse("#F87171")),
        _ => new SolidColorBrush(Color.Parse("#93A9C9"))
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
}
