using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows.Input;
using Avalonia;
using Avalonia.Threading;
using ClearC.Core.Formatting;
using ClearC.Core.Models;
using ClearC.Core.Safety;
using ClearC.Core.Selection;
using ClearC.Core.Services;
using ClearC.Desktop.Infrastructure.Logging;
using ClearC.Desktop.Infrastructure.Windows;
using ClearC.Desktop.Themes;
using ReactiveUI;

namespace ClearC.Desktop.ViewModels;

public sealed class MainWindowViewModel : ReactiveObject
{
    /// <summary>Toast 自动消失时间（§2.6）。</summary>
    public static readonly TimeSpan ToastDuration = TimeSpan.FromSeconds(6);

    private readonly ICleanupScanner _scanner;
    private readonly ICleanupExecutor _executor;
    private readonly CleanupSafetyPolicy _safetyPolicy;
    private readonly InMemoryLogStore _logStore;
    private readonly IElevationService _elevationService;
    private readonly IThemePalette _palette;
    private readonly IToastScheduler _toastScheduler;
    private readonly ILocationOpener _locationOpener;
    private readonly Dictionary<CleanupDisplayGroup, CleanupGroupViewModel> _groupCache = [];
    private CleanupSelection? _selection;
    private CancellationTokenSource? _operationCancellation;
    private TaskCompletionSource? _operationDone;
    private WorkflowState _state;
    private DiskSnapshot _disk;
    private double _progressValue;
    private string _progressText = string.Empty;
    private long _measuredFreedBytes;
    private long _estimatedFreedBytes;
    private bool _isToastVisible;
    private bool _isToastSuccess = true;
    private bool _isFastTierComplete;
    private int _scanTotal;
    private int _scanCompleted;
    private int _pendingTargetCount;
    private bool _isAwaitingCommandStop;
    private bool _hideZeroByteItems = true;
    private bool _skipSystemAnalysis = true;
    private string _ghostText = string.Empty;
    private bool _isCloseConfirmationVisible;

    internal MainWindowViewModel(
        ICleanupScanner scanner,
        ICleanupExecutor executor,
        CleanupSafetyPolicy safetyPolicy,
        DiskSnapshot initialDisk,
        InMemoryLogStore logStore,
        IElevationService? elevationService = null,
        IThemePalette? palette = null,
        IToastScheduler? toastScheduler = null,
        ILocationOpener? locationOpener = null)
    {
        _scanner = scanner;
        _executor = executor;
        _safetyPolicy = safetyPolicy;
        _logStore = logStore;
        _elevationService = elevationService ?? new ElevationService();
        _palette = palette ?? ThemePalette.Instance;
        _toastScheduler = toastScheduler ?? new DispatcherToastScheduler();
        _locationOpener = locationOpener ?? new LocationOpener();
        _disk = initialDisk;

        PrimaryCommand = ReactiveCommand.CreateFromTask(HandlePrimaryAsync);
        SecondaryCommand = ReactiveCommand.CreateFromTask(HandleSecondaryAsync);
        CancelConfirmationCommand = ReactiveCommand.Create(CancelConfirmation);
        ConfirmCleanupCommand = ReactiveCommand.CreateFromTask(ConfirmCleanupAsync);
        ClearDeniedCommand = ReactiveCommand.Create(ClearDenied);
        ClearLogCommand = ReactiveCommand.Create(_logStore.Clear);
        RestartElevatedCommand = ReactiveCommand.Create(RestartElevated);
        CancelCloseCommand = ReactiveCommand.Create(() => IsCloseConfirmationVisible = false);
        ConfirmCloseCommand = ReactiveCommand.CreateFromTask(ConfirmCloseAsync);

        TitleBar = new TitleBarViewModel();
        Workspace = new CleanupWorkspaceViewModel(this);
        LogPanel = new LogPanelViewModel(this, logStore, _palette);
        StatusBar = new StatusBarViewModel(this);
        Overlay = new WorkflowOverlayViewModel(this);

        State = WorkflowState.Idle;
        AddLog("OK", $"ClearC 引擎初始化完成 · v{ProductVersion}");
        AddLog("INFO", $"挂载磁盘 {_disk.DriveName} · {_disk.DriveFormat} · {ByteSizeFormatter.Format(_disk.TotalBytes)}");
        AddLog("INFO", $"已用 {ByteSizeFormatter.Format(_disk.UsedBytes)} · 可用 {ByteSizeFormatter.Format(_disk.FreeBytes)} · 占用率 {_disk.UsedRatio:P1}");
        if (!_elevationService.IsElevated)
        {
            AddLog("WARN", "当前未提权 · 部分系统缓存不可清理，可使用「以管理员重启」。");
        }

        AddLog("INFO", "等待指令 … 点击「扫描分析」开始扫描");
    }

    /// <summary>「停止并退出」完成、可以真正关闭窗口时触发。</summary>
    public event EventHandler? CloseRequested;

    /// <summary>全部扫描项（含 0 字节项），供分组与安全策略使用。</summary>
    public ObservableCollection<CleanupItemViewModel> Items { get; } = [];

    /// <summary>按展示分组排好序的可见行。</summary>
    public ObservableCollection<CleanupGroupViewModel> Groups { get; } = [];

    public ObservableCollection<CleanupItemViewModel> SelectedItems { get; } = [];

    public TitleBarViewModel TitleBar { get; }
    public CleanupWorkspaceViewModel Workspace { get; }
    public LogPanelViewModel LogPanel { get; }
    public StatusBarViewModel StatusBar { get; }
    public WorkflowOverlayViewModel Overlay { get; }

    public ICommand PrimaryCommand { get; }
    public ICommand SecondaryCommand { get; }
    public ICommand CancelConfirmationCommand { get; }
    public ICommand ConfirmCleanupCommand { get; }
    public ICommand ClearDeniedCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand RestartElevatedCommand { get; }
    public ICommand CancelCloseCommand { get; }
    public ICommand ConfirmCloseCommand { get; }

    public WorkflowState State
    {
        get => _state;
        private set
        {
            if (_state == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _state, value);
            UpdateRowSelectability();
            RefreshStateProperties();
        }
    }

    public string DriveTitle => $"{_disk.DriveName} 系统盘";
    public string DriveInfo => $"SSD · {ByteSizeFormatter.Format(_disk.TotalBytes)} · {_disk.DriveFormat}";
    public double UsedRatio => _disk.UsedRatio;
    public string UsedPercent => $"{Math.Round(_disk.UsedRatio * 100):0}%";
    public string DiskUsedText => ByteSizeFormatter.Format(_disk.UsedBytes);
    public string DiskFreeText => ByteSizeFormatter.Format(_disk.FreeBytes);

    public string HeroLabel => State switch
    {
        WorkflowState.Cleaning => "已释放",
        WorkflowState.Done => "本次已释放",
        _ => "可释放空间"
    };

    public string HeroValue => State switch
    {
        WorkflowState.Idle => "待扫描",
        WorkflowState.Scanning => "正在分析…",
        WorkflowState.Cleaning or WorkflowState.Done => FreedText,
        _ => ByteSizeFormatter.Format(SelectedBytes)
    };

    /// <summary>实测 + 估算（估算单独标注，§2.1）。</summary>
    private string FreedText => _estimatedFreedBytes > 0
        ? $"{ByteSizeFormatter.Format(_measuredFreedBytes)} + ~{ByteSizeFormatter.Format(_estimatedFreedBytes)}"
        : ByteSizeFormatter.Format(_measuredFreedBytes);

    public bool IsHeroPlaceholder => State is WorkflowState.Idle or WorkflowState.Scanning;

    public string PrimaryButtonText => State switch
    {
        WorkflowState.Idle => "扫描分析",
        WorkflowState.Scanning when _isFastTierComplete => $"执行清理 · {ByteSizeFormatter.Format(SelectedBytes)}",
        WorkflowState.Scanning => "扫描中…",
        WorkflowState.Results => $"执行清理 · {ByteSizeFormatter.Format(SelectedBytes)}",
        WorkflowState.Confirming => $"执行清理 · {ByteSizeFormatter.Format(SelectedBytes)}",
        WorkflowState.Cleaning => "清理中…",
        _ => "执行清理"
    };

    public string PrimaryIconKey => State is WorkflowState.Idle or WorkflowState.Scanning ? "i-search" : "i-clean";

    public bool IsPrimaryGlowing => State == WorkflowState.Idle;

    public bool IsPrimaryEnabled => State switch
    {
        WorkflowState.Idle => true,
        WorkflowState.Scanning => _isFastTierComplete && SelectedCount > 0,
        WorkflowState.Results => SelectedCount > 0,
        _ => false
    };

    public string SecondaryButtonText => State switch
    {
        WorkflowState.Scanning or WorkflowState.Cleaning when _isAwaitingCommandStop => "正在等待当前命令结束…",
        WorkflowState.Scanning or WorkflowState.Cleaning => "取消",
        WorkflowState.Results or WorkflowState.Done => "重新分析",
        _ => "执行清理"
    };

    public bool IsSecondaryEnabled => State switch
    {
        WorkflowState.Scanning or WorkflowState.Cleaning => !_isAwaitingCommandStop,
        WorkflowState.Results or WorkflowState.Done => true,
        _ => false
    };

    /// <summary>取消后正在等待已启动的官方命令安全结束（§2.4）。</summary>
    public bool IsAwaitingCommandStop
    {
        get => _isAwaitingCommandStop;
        private set
        {
            if (_isAwaitingCommandStop == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _isAwaitingCommandStop, value);
            this.RaisePropertyChanged(nameof(SecondaryButtonText));
            this.RaisePropertyChanged(nameof(IsSecondaryEnabled));
        }
    }

    public bool IsProgressVisible => State is WorkflowState.Scanning or WorkflowState.Cleaning;

    public double ProgressValue
    {
        get => _progressValue;
        private set
        {
            this.RaiseAndSetIfChanged(ref _progressValue, value);
            this.RaisePropertyChanged(nameof(ProgressBarWidth));
        }
    }

    public string ProgressText
    {
        get => _progressText;
        private set
        {
            this.RaiseAndSetIfChanged(ref _progressText, value);
            this.RaisePropertyChanged(nameof(ProgressBarWidth));
        }
    }

    /// <summary>进度条填充宽度（轨道 230px，原型 <c>.pbar</c>）。</summary>
    public double ProgressBarWidth => Math.Clamp(ProgressValue, 0, 100) / 100 * 230;

    public int SelectedCount => Items.Count(item => item.IsSelected);
    public long SelectedBytes => Items.Where(item => item.IsSelected).Sum(item => item.Model.SizeBytes);
    public string SelectedSummary => SelectedCount == 0 ? "未选择" : $"已选 {SelectedCount} 项 · {ByteSizeFormatter.Format(SelectedBytes)}";

    /// <summary>快档完成后即可交互（§3.3）。</summary>
    public bool CanInteractWithList => State == WorkflowState.Results || State is WorkflowState.Scanning && _isFastTierComplete;

    private IEnumerable<CleanupItemViewModel> VisibleRows => Groups.SelectMany(group => group.Items);

    public bool CanSelectAll => CanInteractWithList && VisibleRows.Any(row => row.Model.CanClean);

    public bool IsAllSelected
    {
        get
        {
            var selectable = VisibleRows.Where(row => row.Model.CanClean).ToArray();
            return selectable.Length > 0 && selectable.All(row => row.IsSelected);
        }
        set
        {
            if (!CanSelectAll)
            {
                return;
            }

            foreach (var row in VisibleRows)
            {
                // 只影响当前可见且可清理的行（§5.6）：勾选规则以 CleanupSelection 为唯一来源。
                var selected = value && row.CanSelect;
                _selection?.SetSelected(row.Id, selected);
                row.IsSelected = selected;
            }

            RefreshSelectionProperties();
        }
    }

    /// <summary>隐藏 0 字节项（默认开，§5.6）。</summary>
    public bool HideZeroByteItems
    {
        get => _hideZeroByteItems;
        set
        {
            if (_hideZeroByteItems == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _hideZeroByteItems, value);
            RefreshGroups();
        }
    }

    /// <summary>
    /// 快速模式（默认开）：跳过需要拉子进程的 DISM 组件存储与 vssadmin 卷影副本分析。
    /// 它们各自可能等上几十秒，未提权时还拿不到数字；要看数字时取消勾选即可。
    /// </summary>
    public bool SkipSystemAnalysis
    {
        get => _skipSystemAnalysis;
        set => this.RaiseAndSetIfChanged(ref _skipSystemAnalysis, value);
    }

    public bool IsEmptyVisible => State == WorkflowState.Idle && Items.Count == 0;
    public bool IsListVisible => Groups.Count > 0;
    public bool IsSweepVisible => State == WorkflowState.Scanning;

    public string GhostText
    {
        get => _ghostText;
        private set => this.RaiseAndSetIfChanged(ref _ghostText, value);
    }

    public bool IsGhostVisible => State == WorkflowState.Scanning && _ghostText.Length > 0;

    public bool IsConfirmationVisible => State == WorkflowState.Confirming && !IsCloseConfirmationVisible;
    public bool HasRiskSelection => SelectedItems.Any(item => item.Model.Risk != CleanupRisk.Low);
    public bool HasCodexConversationSelection => SelectedItems.Any(item => item.Model.CleanerKey == "codex-conversations");

    public bool HasDeniedItems => Items.Any(item => item.IsDenied);

    public string ConfirmationWarning => HasCodexConversationSelection
        ? "Codex 活动与归档会话文件将被永久删除。请先关闭 Codex；检测到 Codex 运行时会跳过。此操作无法恢复。"
        : HasRiskSelection
        ? "包含中高风险项目，清理后可能无法恢复或需要重新下载，请确认。"
        : "清理会永久删除所选缓存内容，请确认后继续。";

    public bool IsConfirmationWarningVisible => HasRiskSelection || HasCodexConversationSelection || HasDeniedItems;

    public string ConfirmationTotal => ByteSizeFormatter.Format(SelectedBytes);
    public bool IsConfirmEnabled => !HasDeniedItems && SelectedCount > 0;

    /// <summary>慢档被中断时，确认页提示未完成分析的项数（§3.3）。</summary>
    public string ConfirmationPendingNote => _pendingTargetCount > 0
        ? $"另有 {_pendingTargetCount} 项未完成分析，本次不会清理。"
        : string.Empty;

    public bool IsPendingNoteVisible => _pendingTargetCount > 0;

    public bool IsToastVisible
    {
        get => _isToastVisible;
        private set => this.RaiseAndSetIfChanged(ref _isToastVisible, value);
    }

    public bool IsToastSuccess => _isToastSuccess;

    public string ToastText => _estimatedFreedBytes > 0
        ? $"清理完成 · 实测释放 {ByteSizeFormatter.Format(_measuredFreedBytes)} · 预计再释放 ~{ByteSizeFormatter.Format(_estimatedFreedBytes)}"
        : $"清理完成 · 释放 {ByteSizeFormatter.Format(_measuredFreedBytes)}";

    public bool IsCloseConfirmationVisible
    {
        get => _isCloseConfirmationVisible;
        private set
        {
            if (_isCloseConfirmationVisible != value)
            {
                this.RaiseAndSetIfChanged(ref _isCloseConfirmationVisible, value);
                this.RaisePropertyChanged(nameof(IsConfirmationVisible));
            }
        }
    }

    public string CloseConfirmationText => State == WorkflowState.Scanning
        ? "扫描正在进行，关闭将丢失本次扫描结果。"
        : "清理正在进行，此时关闭可能造成半清理。";

    public bool IsElevationBannerVisible => !_elevationService.IsElevated;
    public string ElevationBannerText => "未提权 · 部分系统缓存不可清理";

    public string StatusText => State switch
    {
        WorkflowState.Idle => "SYSTEM READY · 等待指令",
        WorkflowState.Scanning => "SCANNING · 正在扫描 C 盘",
        WorkflowState.Results => "SCAN COMPLETE · 扫描完成",
        WorkflowState.Confirming => "AWAIT CONFIRM · 等待确认",
        WorkflowState.Cleaning => "CLEANING · 正在清理",
        _ => "TASK COMPLETE · 清理完成"
    };

    public Avalonia.Media.IBrush StatusBrush => State switch
    {
        WorkflowState.Confirming => _palette.Brush("ClearCAmber"),
        WorkflowState.Scanning or WorkflowState.Cleaning => _palette.Brush("ClearCAccent"),
        _ => _palette.Brush("ClearCGreen")
    };

    public bool IsStatusPulsing => State is WorkflowState.Scanning or WorkflowState.Cleaning;

    public string StateCode => $"CLEARC · v{ProductVersion} · STATE {(int)State + 1:00}/06";

    private static string ProductVersion
    {
        get
        {
            var informational = typeof(MainWindowViewModel).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return string.IsNullOrWhiteSpace(informational)
                ? "0.0.0"
                : informational.Split('+')[0];
        }
    }

    /// <summary>窗口关闭前调用：停掉 Toast 定时器（§2.6、§11）。</summary>
    public void StopToastTimer()
    {
        _toastScheduler.Cancel();
        IsToastVisible = false;
    }

    /// <summary>扫描/清理进行中关闭窗口时，由 <c>CloseGuard</c> 触发。</summary>
    public void RequestCloseConfirmation() => IsCloseConfirmationVisible = true;

    /// <summary>
    /// Esc 关闭当前模态（关闭保护优先于确认页）。
    /// 返回 false 表示当前没有可关闭的模态，按键交给系统处理。
    /// </summary>
    public bool TryDismissModal()
    {
        if (IsCloseConfirmationVisible)
        {
            IsCloseConfirmationVisible = false;
            return true;
        }

        if (IsConfirmationVisible)
        {
            CancelConfirmation();
            return true;
        }

        return false;
    }

    /// <summary>
    /// 系统关机/注销路径：不能阻塞 UI 线程等待，只取消进行中的任务并停掉 Toast 定时器（§3.2）。
    /// </summary>
    public void PrepareForShutdown()
    {
        _operationCancellation?.Cancel();
        StopToastTimer();
    }

    private async Task HandlePrimaryAsync()
    {
        switch (State)
        {
            case WorkflowState.Idle:
                await ScanAsync();
                break;
            case WorkflowState.Scanning when _isFastTierComplete:
                // 慢档仍在跑：先停掉慢档、等当前项结束、保留已完成行，再进确认页（§3.3）。
                await StopOperationAndWaitAsync();
                EnterConfirmation();
                break;
            case WorkflowState.Results when SelectedCount > 0:
                EnterConfirmation();
                break;
        }
    }

    private async Task HandleSecondaryAsync()
    {
        if (State is WorkflowState.Scanning or WorkflowState.Cleaning)
        {
            CancelOperation();
            return;
        }

        if (State is WorkflowState.Results or WorkflowState.Done)
        {
            await ScanAsync();
        }
    }

    private void CancelOperation()
    {
        if (_operationCancellation is not { IsCancellationRequested: false })
        {
            return;
        }

        AddLog("WARN", "已请求取消，等待已启动的官方命令安全结束 …");
        IsAwaitingCommandStop = true;
        _operationCancellation.Cancel();
    }

    private async Task StopOperationAndWaitAsync()
    {
        _operationCancellation?.Cancel();
        await WaitForOperationAsync();
        IsAwaitingCommandStop = false;
    }

    private async Task WaitForOperationAsync()
    {
        var done = _operationDone;
        if (done is not null)
        {
            await done.Task;
        }
    }

    private void EnterConfirmation()
    {
        _pendingTargetCount = Math.Max(0, _scanTotal - _scanCompleted);
        RebuildSelectedItems();

        var selectedIds = SelectedItems.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var evaluations = _safetyPolicy.EvaluateForPlan(Items.Select(row => row.Model), selectedIds, selectedIds);
        foreach (var row in Items)
        {
            row.DeniedReason = null;
        }

        var denied = 0;
        foreach (var (model, decision) in evaluations.Where(entry => entry.Decision.Kind != SafetyDecisionKind.Allowed))
        {
            denied++;
            if (Items.FirstOrDefault(row => row.Id == model.Id) is { } row)
            {
                row.DeniedReason = decision.Reason;
            }

            AddLog("WARN", $"不可清理：{model.DisplayName} · {decision.Reason}");
        }

        State = WorkflowState.Confirming;
        AddLog("INFO", denied > 0
            ? $"等待确认：{SelectedCount} 项 · 其中 {denied} 项被安全策略拒绝，请先取消选择。"
            : $"等待确认：{SelectedCount} 项 · {ByteSizeFormatter.Format(SelectedBytes)}");
        RefreshStateProperties();
    }

    private void CancelConfirmation()
    {
        if (State != WorkflowState.Confirming)
        {
            return;
        }

        ClearDenied();
        State = WorkflowState.Results;
        AddLog("INFO", "已取消清理确认。");
    }

    private void ClearDenied()
    {
        foreach (var row in Items.Where(item => item.IsDenied).ToArray())
        {
            row.DeniedReason = null;
            row.IsSelected = false;
        }

        this.RaisePropertyChanged(nameof(HasDeniedItems));
        this.RaisePropertyChanged(nameof(IsConfirmationWarningVisible));
        this.RaisePropertyChanged(nameof(IsConfirmEnabled));
        RefreshSelectionProperties();
    }

    private void RestartElevated()
    {
        if (_elevationService.IsElevated)
        {
            return;
        }

        AddLog("INFO", "正在请求以管理员身份重启 …");
        _operationCancellation?.Cancel();
        if (_elevationService.RestartElevated())
        {
            AddLog("OK", "已启动提权实例，当前实例即将退出。");
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            AddLog("WARN", "已取消提权，保持当前权限继续运行。");
        }
    }

    private async Task ConfirmCloseAsync()
    {
        IsCloseConfirmationVisible = false;
        AddLog("WARN", "正在停止进行中的任务并退出 …");
        _operationCancellation?.Cancel();
        var done = _operationDone;
        if (done is not null)
        {
            var finished = await Task.WhenAny(done.Task, Task.Delay(CloseGuard.GracefulStopTimeout));
            if (finished != done.Task)
            {
                AddLog("WARN", "当前命令仍在运行，将继续等待其安全结束。");
            }

            await done.Task;
        }

        StopToastTimer();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private async Task ScanAsync()
    {
        _operationCancellation?.Dispose();
        _operationCancellation = new CancellationTokenSource();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _operationDone = completion;

        StopToastTimer();
        Items.Clear();
        Groups.Clear();
        foreach (var group in _groupCache.Values)
        {
            group.Refresh([]);
        }

        SelectedItems.Clear();
        _selection = null;
        _measuredFreedBytes = 0;
        _estimatedFreedBytes = 0;
        _isFastTierComplete = false;
        _pendingTargetCount = 0;
        _scanTotal = 0;
        _scanCompleted = 0;
        IsAwaitingCommandStop = false;
        GhostText = string.Empty;
        ProgressValue = 0;
        ProgressText = "准备扫描…";
        State = WorkflowState.Scanning;
        AddLog("INFO", "开始扫描 C 盘 …");

        var progress = new CallbackProgress<ScanProgress>(OnScanProgress);
        try
        {
            var result = await _scanner.ScanAsync(progress, _operationCancellation.Token, SkipSystemAnalysis);
            _disk = result.Disk;
            foreach (var model in result.Items)
            {
                EnsureRow(model);
            }

            _selection = new CleanupSelection(Items.Select(row => row.Model));
            foreach (var row in Items)
            {
                row.SetInitialSelection(_selection.IsSelected(row.Id));
            }

            _isFastTierComplete = true;
            _scanCompleted = _scanTotal;
            _pendingTargetCount = 0;
            GhostText = string.Empty;
            RebuildSelectedItems();
            RefreshGroups();
            State = WorkflowState.Results;
            AddLog("OK", $"扫描完成 · 耗时 {result.Elapsed.TotalSeconds:0.0}s · 定位 {result.Items.Sum(item => item.FileCount):N0} 个文件");
            AddLog("INFO", $"共 {result.Items.Count} 个目标位置 · 总占用 {ByteSizeFormatter.Format(result.TotalBytes)}");
            AddLog("INFO", $"可清理 {Items.Count(row => row.Model.CanClean)} 项 · 共 {ByteSizeFormatter.Format(Items.Where(row => row.Model.CanClean).Sum(row => row.Model.SizeBytes))}");
            AddLog("INFO", "已默认勾选低风险项目，可手动调整。");
        }
        catch (OperationCanceledException)
        {
            // 取消后保留已完成的行（§2.4、§3.3）。
            GhostText = string.Empty;
            _selection = new CleanupSelection(Items.Select(row => row.Model));
            foreach (var row in Items)
            {
                row.SetInitialSelection(_selection.IsSelected(row.Id));
            }

            State = WorkflowState.Idle;
            AddLog("WARN", $"扫描已取消，已完成的 {Items.Count} 项目标保留在列表中。");
        }
        catch (Exception exception)
        {
            GhostText = string.Empty;
            State = WorkflowState.Idle;
            AddLog("ERR", $"扫描失败：{exception.Message}", exception);
        }
        finally
        {
            IsAwaitingCommandStop = false;
            RefreshGroups();
            RefreshStateProperties();
            _operationDone = null;
            completion.TrySetResult();
        }
    }

    private void OnScanProgress(ScanProgress value) => OnUi(() =>
    {
        _scanTotal = value.Total;
        _scanCompleted = value.Completed;
        ProgressValue = value.Ratio * 100;
        ProgressText = value.DisplayText;
        GhostText = value.Item is null ? value.GhostText : string.Empty;

        if (value.Tier == ScanTier.Slow)
        {
            MarkFastTierComplete();
        }

        if (value.Item is { } item)
        {
            EnsureRow(item);
            RebuildSelectedItems();
            RefreshGroups();
            AddLog(
                item.SizeBytes > 0 ? "INFO" : "WARN",
                $"[{value.Completed:00}/{value.Total:00}] {item.DisplayName} · {item.FileCount:N0} 个文件 · {ByteSizeFormatter.Format(item.SizeBytes)}{Note(item)}");
        }

        this.RaisePropertyChanged(nameof(IsGhostVisible));
        this.RaisePropertyChanged(nameof(IsSweepVisible));
    });

    private static string Note(CleanupItem item) => string.IsNullOrWhiteSpace(item.ScanNote) ? string.Empty : $" · {item.ScanNote}";

    private void MarkFastTierComplete()
    {
        if (_isFastTierComplete)
        {
            return;
        }

        _isFastTierComplete = true;
        AddLog("INFO", "快档扫描完成，可以先查看结果并勾选；慢速目标仍在后台继续分析。");
        UpdateRowSelectability();
        RefreshStateProperties();
    }

    private CleanupItemViewModel EnsureRow(CleanupItem model)
    {
        var normalized = Normalize(model);
        if (Items.FirstOrDefault(row => row.Id == normalized.Id) is { } existing)
        {
            return existing;
        }

        var row = new CleanupItemViewModel(normalized, _palette, _elevationService.IsElevated, _locationOpener)
        {
            CanSelect = CanInteractWithList
        };
        row.SelectionChanged += OnItemSelectionChanged;
        Items.Add(row);
        return row;
    }

    /// <summary>
    /// 扫描探测可能给出相对路径（CLI 探测的输出）；执行侧一律用绝对路径做白名单校验，
    /// 所以入库前统一补齐，避免"扫描有结果、清理被跳过"。
    /// </summary>
    private static CleanupItem Normalize(CleanupItem item)
    {
        if (item.Paths is not { Count: > 0 } paths)
        {
            return item;
        }

        var normalized = paths
            .Select(path => Path.IsPathFullyQualified(path) ? path : Path.GetFullPath(path))
            .ToArray();
        return normalized.SequenceEqual(paths, StringComparer.Ordinal) ? item : item with { Paths = normalized };
    }

    private void OnItemSelectionChanged(object? sender, EventArgs e)
    {
        if (sender is CleanupItemViewModel row)
        {
            _selection?.SetSelected(row.Id, row.IsSelected);
        }

        RebuildSelectedItems();
        RefreshSelectionProperties();
    }

    /// <summary>按 §6.4 排序重建分组：组间按小计降序、组内按大小降序（0 字节排末尾）。</summary>
    private void RefreshGroups()
    {
        var visible = Items.Where(row => !HideZeroByteItems || row.Model.SizeBytes > 0).ToArray();
        var ordered = visible
            .GroupBy(row => row.DisplayGroup)
            .Select(group => (
                Group: group.Key,
                Rows: (IReadOnlyList<CleanupItemViewModel>)group
                    .OrderByDescending(row => row.Model.SizeBytes)
                    .ThenBy(row => row.DisplayName, StringComparer.Ordinal)
                    .ToArray()))
            .OrderByDescending(entry => entry.Rows.Sum(row => row.Model.SizeBytes))
            .ThenBy(entry => (int)entry.Group)
            .ToArray();

        foreach (var cached in _groupCache.Values)
        {
            cached.Refresh([]);
        }

        Groups.Clear();
        foreach (var (group, rows) in ordered)
        {
            if (!_groupCache.TryGetValue(group, out var groupViewModel))
            {
                groupViewModel = new CleanupGroupViewModel(group);
                _groupCache[group] = groupViewModel;
            }

            groupViewModel.Refresh(rows);
            Groups.Add(groupViewModel);
        }

        this.RaisePropertyChanged(nameof(IsListVisible));
        this.RaisePropertyChanged(nameof(IsEmptyVisible));
        this.RaisePropertyChanged(nameof(CanSelectAll));
        this.RaisePropertyChanged(nameof(IsAllSelected));
    }

    private void UpdateRowSelectability()
    {
        var selectable = CanInteractWithList;
        foreach (var row in Items)
        {
            row.CanSelect = selectable;
        }
    }

    private void RebuildSelectedItems()
    {
        SelectedItems.Clear();
        foreach (var item in Items.Where(item => item.IsSelected))
        {
            SelectedItems.Add(item);
        }

        this.RaisePropertyChanged(nameof(HasRiskSelection));
        this.RaisePropertyChanged(nameof(HasCodexConversationSelection));
        this.RaisePropertyChanged(nameof(ConfirmationWarning));
        this.RaisePropertyChanged(nameof(IsConfirmationWarningVisible));
        this.RaisePropertyChanged(nameof(ConfirmationTotal));
        this.RaisePropertyChanged(nameof(IsConfirmEnabled));
        this.RaisePropertyChanged(nameof(SelectedCount));
        this.RaisePropertyChanged(nameof(SelectedBytes));
        this.RaisePropertyChanged(nameof(SelectedSummary));
    }

    private async Task ConfirmCleanupAsync()
    {
        if (!IsConfirmEnabled)
        {
            return;
        }

        var selectedIds = SelectedItems.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        IReadOnlyList<CleanupItem> plan;
        try
        {
            plan = _safetyPolicy.BuildPlan(Items.Select(row => row.Model), selectedIds, selectedIds);
        }
        catch (InvalidOperationException exception)
        {
            AddLog("ERR", exception.Message, exception);
            State = WorkflowState.Results;
            return;
        }

        _operationCancellation?.Dispose();
        _operationCancellation = new CancellationTokenSource();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _operationDone = completion;

        _measuredFreedBytes = 0;
        _estimatedFreedBytes = 0;
        IsAwaitingCommandStop = false;
        ProgressValue = 0;
        ProgressText = "准备清理…";
        State = WorkflowState.Cleaning;
        AddLog("INFO", $"开始清理 {plan.Count} 项 · 预计释放 {ByteSizeFormatter.Format(plan.Sum(item => item.SizeBytes))}");

        var progress = new CallbackProgress<CleanupProgress>(OnCleanupProgress);
        try
        {
            var result = await _executor.CleanAsync(plan, progress, _operationCancellation.Token);
            _measuredFreedBytes = result.MeasuredFreedBytes;
            _estimatedFreedBytes = result.EstimatedFreedBytes;
            _disk = _disk with { FreeBytes = Math.Min(_disk.TotalBytes, _disk.FreeBytes + result.FreedBytes) };
            State = WorkflowState.Done;
            ShowToast();
            AddLog("OK", $"清理完成 · 成功 {result.CompletedCount} 项 · 实测释放 {ByteSizeFormatter.Format(result.MeasuredFreedBytes)}");
            if (result.EstimatedFreedBytes > 0)
            {
                AddLog("INFO", $"官方命令项按扫描时大小估算，预计再释放 ~{ByteSizeFormatter.Format(result.EstimatedFreedBytes)}（估算）。");
            }

            AddLog("INFO", $"{_disk.DriveName} 可用空间为估算值，建议重新扫描获取精确值。");
        }
        catch (OperationCanceledException)
        {
            State = WorkflowState.Done;
            AddLog("WARN", "已停止后续清理任务；正在执行的官方命令已等待其安全结束。");
        }
        catch (Exception exception)
        {
            State = WorkflowState.Done;
            AddLog("ERR", $"清理任务失败：{exception.Message}", exception);
        }
        finally
        {
            IsAwaitingCommandStop = false;
            RebuildSelectedItems();
            RefreshGroups();
            RefreshStateProperties();
            _operationDone = null;
            completion.TrySetResult();
        }
    }

    private void OnCleanupProgress(CleanupProgress value) => OnUi(() =>
    {
        ProgressValue = value.Ratio * 100;
        var displayIndex = value.Result is null ? value.Completed + 1 : value.Completed;
        // 清空回收站走 Shell API，一旦开始就无法中断（§3.1），文案要如实说明。
        ProgressText = $"{Math.Min(displayIndex, value.Total):00}/{value.Total:00} · {value.Item.DisplayName}"
            + (value.Item.CleanerKind == CleanerKind.RecycleBin ? "（清空过程无法中断）" : string.Empty);
        var row = Items.FirstOrDefault(item => item.Id == value.Item.Id);
        foreach (var item in Items)
        {
            item.IsCurrent = item == row && value.Result is null;
        }

        if (value.Result is null)
        {
            AddLog("INFO", $"[{value.Completed + 1:00}/{value.Total:00}] 清理 {value.Item.DisplayName} …");
            return;
        }

        if (row is null)
        {
            return;
        }

        row.ApplyResult(value.Result);
        if (value.Result.IsEstimated)
        {
            _estimatedFreedBytes += value.Result.FreedBytes;
        }
        else
        {
            _measuredFreedBytes += value.Result.FreedBytes;
        }

        this.RaisePropertyChanged(nameof(HeroValue));
        RebuildSelectedItems();
        AddLog(value.Result.Outcome switch
        {
            CleanupOutcome.Completed => "OK",
            CleanupOutcome.Failed => "ERR",
            _ => "WARN"
        }, $"{value.Item.DisplayName}：{value.Result.Message}");
    });

    private void ShowToast()
    {
        IsToastVisible = true;
        _toastScheduler.Schedule(ToastDuration, () =>
        {
            IsToastVisible = false;
        });
    }

    private void AddLog(string level, string message, Exception? exception = null)
    {
        switch (level)
        {
            case "WARN":
                _logStore.Warning(message, exception);
                break;
            case "ERR":
                _logStore.Error(message, exception);
                break;
            default:
                _logStore.Information(message);
                break;
        }
    }

    private void RefreshSelectionProperties()
    {
        this.RaisePropertyChanged(nameof(SelectedCount));
        this.RaisePropertyChanged(nameof(SelectedBytes));
        this.RaisePropertyChanged(nameof(SelectedSummary));
        this.RaisePropertyChanged(nameof(IsAllSelected));
        this.RaisePropertyChanged(nameof(HeroValue));
        this.RaisePropertyChanged(nameof(PrimaryButtonText));
        this.RaisePropertyChanged(nameof(IsPrimaryEnabled));
        this.RaisePropertyChanged(nameof(ConfirmationTotal));
        this.RaisePropertyChanged(nameof(IsConfirmEnabled));
        this.RaisePropertyChanged(nameof(HasDeniedItems));
    }

    private void RefreshStateProperties()
    {
        this.RaisePropertyChanged(nameof(DriveTitle));
        this.RaisePropertyChanged(nameof(DriveInfo));
        this.RaisePropertyChanged(nameof(UsedRatio));
        this.RaisePropertyChanged(nameof(UsedPercent));
        this.RaisePropertyChanged(nameof(DiskUsedText));
        this.RaisePropertyChanged(nameof(DiskFreeText));
        this.RaisePropertyChanged(nameof(HeroLabel));
        this.RaisePropertyChanged(nameof(HeroValue));
        this.RaisePropertyChanged(nameof(IsHeroPlaceholder));
        this.RaisePropertyChanged(nameof(PrimaryButtonText));
        this.RaisePropertyChanged(nameof(PrimaryIconKey));
        this.RaisePropertyChanged(nameof(IsPrimaryGlowing));
        this.RaisePropertyChanged(nameof(IsPrimaryEnabled));
        this.RaisePropertyChanged(nameof(SecondaryButtonText));
        this.RaisePropertyChanged(nameof(IsSecondaryEnabled));
        this.RaisePropertyChanged(nameof(IsProgressVisible));
        this.RaisePropertyChanged(nameof(CanSelectAll));
        this.RaisePropertyChanged(nameof(CanInteractWithList));
        this.RaisePropertyChanged(nameof(IsEmptyVisible));
        this.RaisePropertyChanged(nameof(IsListVisible));
        this.RaisePropertyChanged(nameof(IsSweepVisible));
        this.RaisePropertyChanged(nameof(IsGhostVisible));
        this.RaisePropertyChanged(nameof(IsConfirmationVisible));
        this.RaisePropertyChanged(nameof(IsConfirmationWarningVisible));
        this.RaisePropertyChanged(nameof(IsConfirmEnabled));
        this.RaisePropertyChanged(nameof(ConfirmationPendingNote));
        this.RaisePropertyChanged(nameof(IsPendingNoteVisible));
        this.RaisePropertyChanged(nameof(HasDeniedItems));
        this.RaisePropertyChanged(nameof(CloseConfirmationText));
        this.RaisePropertyChanged(nameof(StatusText));
        this.RaisePropertyChanged(nameof(StatusBrush));
        this.RaisePropertyChanged(nameof(IsStatusPulsing));
        this.RaisePropertyChanged(nameof(StateCode));
        this.RaisePropertyChanged(nameof(ToastText));
        this.RaisePropertyChanged(nameof(IsToastSuccess));
        this.RaisePropertyChanged(nameof(IsElevationBannerVisible));
        RefreshSelectionProperties();
    }

    private static void OnUi(Action action)
    {
        // 并行扫描会在工作线程上报进度；没有 Avalonia 上下文时（纯单元测试）直接同步执行。
        if (Application.Current is null || Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
