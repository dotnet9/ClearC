using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Media;
using ClearC.Core.Formatting;
using ClearC.Core.Models;
using ClearC.Core.Safety;
using ClearC.Core.Selection;
using ClearC.Core.Services;
using ReactiveUI;

namespace ClearC.Desktop.ViewModels;

public sealed class MainWindowViewModel : ReactiveObject
{
    private readonly ICleanupScanner _scanner;
    private readonly ICleanupExecutor _executor;
    private readonly CleanupSafetyPolicy _safetyPolicy;
    private readonly DiskSnapshot _initialDisk;
    private WorkflowState _state;
    private DiskSnapshot _disk;
    private CategoryFilterViewModel? _activeFilter;
    private CancellationTokenSource? _operationCancellation;
    private double _progressValue;
    private string _progressText = string.Empty;
    private long _freedBytes;
    private bool _isToastVisible;

    public MainWindowViewModel(
        ICleanupScanner scanner,
        ICleanupExecutor executor,
        CleanupSafetyPolicy safetyPolicy,
        DiskSnapshot initialDisk)
    {
        _scanner = scanner;
        _executor = executor;
        _safetyPolicy = safetyPolicy;
        _initialDisk = initialDisk;
        _disk = initialDisk;

        Filters = new([
            new(null, "全部"),
            new(CleanupCategory.PackageCache, "包缓存"),
            new(CleanupCategory.TemporaryFiles, "临时文件"),
            new(CleanupCategory.RecycleBin, "回收站"),
            new(CleanupCategory.BrowserCache, "浏览器"),
            new(CleanupCategory.ApplicationData, "应用数据"),
            new(CleanupCategory.SystemFiles, "系统文件")
        ]);
        _activeFilter = Filters[0];
        _activeFilter.IsActive = true;

        PrimaryCommand = ReactiveCommand.CreateFromTask(HandlePrimaryAsync);
        SecondaryCommand = ReactiveCommand.CreateFromTask(HandleSecondaryAsync);
        SelectFilterCommand = ReactiveCommand.Create<CategoryFilterViewModel>(SelectFilter);
        CancelConfirmationCommand = ReactiveCommand.Create(CancelConfirmation);
        ConfirmCleanupCommand = ReactiveCommand.CreateFromTask(ConfirmCleanupAsync);
        ClearLogsCommand = ReactiveCommand.Create(ClearLogs);

        TitleBar = new TitleBarViewModel();
        Workspace = new CleanupWorkspaceViewModel(this);
        LogPanel = new LogPanelViewModel(this);
        StatusBar = new StatusBarViewModel(this);
        Overlay = new WorkflowOverlayViewModel(this);

        State = WorkflowState.Idle;
        AddLog("OK", "ClearC 引擎初始化完成 · v0.1.0");
        AddLog("INFO", $"挂载磁盘 {_disk.DriveName} · {_disk.DriveFormat} · {ByteSizeFormatter.Format(_disk.TotalBytes)}");
        AddLog("INFO", $"已用 {ByteSizeFormatter.Format(_disk.UsedBytes)} · 可用 {ByteSizeFormatter.Format(_disk.FreeBytes)} · 占用率 {_disk.UsedRatio:P1}");
        AddLog("INFO", "等待指令 … 点击「扫描分析」开始扫描");
    }

    public ObservableCollection<CleanupItemViewModel> Items { get; } = [];
    public ObservableCollection<CleanupItemViewModel> VisibleItems { get; } = [];
    public ObservableCollection<CleanupItemViewModel> SelectedItems { get; } = [];
    public ObservableCollection<CategoryFilterViewModel> Filters { get; }
    public ObservableCollection<LogEntryViewModel> Logs { get; } = [];

    public TitleBarViewModel TitleBar { get; }
    public CleanupWorkspaceViewModel Workspace { get; }
    public LogPanelViewModel LogPanel { get; }
    public StatusBarViewModel StatusBar { get; }
    public WorkflowOverlayViewModel Overlay { get; }

    public ICommand PrimaryCommand { get; }
    public ICommand SecondaryCommand { get; }
    public ICommand SelectFilterCommand { get; }
    public ICommand CancelConfirmationCommand { get; }
    public ICommand ConfirmCleanupCommand { get; }
    public ICommand ClearLogsCommand { get; }

    public WorkflowState State
    {
        get => _state;
        private set
        {
            if (_state != value)
            {
                this.RaiseAndSetIfChanged(ref _state, value);
                foreach (var item in Items)
                {
                    item.CanSelect = value == WorkflowState.Results;
                }

                RefreshStateProperties();
            }
        }
    }

    public string DriveTitle => $"{_disk.DriveName} 系统盘";
    public string DriveInfo => $"SSD · {ByteSizeFormatter.Format(_disk.TotalBytes)} · {_disk.DriveFormat}";
    public double UsedRatio => _disk.UsedRatio;
    public string UsedPercent => $"{Math.Round(_disk.UsedRatio * 100):0}%";
    public string DiskUsage => $"已用 {ByteSizeFormatter.Format(_disk.UsedBytes)}  ·  可用 {ByteSizeFormatter.Format(_disk.FreeBytes)}";
    public string HeroLabel => State == WorkflowState.Done ? "本次已释放" : "可释放空间";
    public string HeroValue => State switch
    {
        WorkflowState.Idle => "待扫描",
        WorkflowState.Scanning => "正在分析…",
        WorkflowState.Done => ByteSizeFormatter.Format(_freedBytes),
        _ => ByteSizeFormatter.Format(SelectedBytes)
    };
    public bool IsHeroPlaceholder => State is WorkflowState.Idle or WorkflowState.Scanning;
    public string PrimaryButtonText => State switch
    {
        WorkflowState.Idle => "⌕  扫描分析",
        WorkflowState.Scanning => "⌕  扫描中…",
        WorkflowState.Results => $"✦  执行清理 · {ByteSizeFormatter.Format(SelectedBytes)}",
        WorkflowState.Confirming => $"✦  执行清理 · {ByteSizeFormatter.Format(SelectedBytes)}",
        WorkflowState.Cleaning => "✦  清理中…",
        _ => "✦  执行清理"
    };
    public bool IsPrimaryEnabled => State == WorkflowState.Idle || State == WorkflowState.Results && SelectedCount > 0;
    public string SecondaryButtonText => State switch
    {
        WorkflowState.Scanning or WorkflowState.Cleaning => "取消",
        WorkflowState.Results or WorkflowState.Done => "重新分析",
        _ => "执行清理"
    };
    public bool IsSecondaryEnabled => State is WorkflowState.Scanning or WorkflowState.Cleaning or WorkflowState.Results or WorkflowState.Done;
    public bool IsProgressVisible => State is WorkflowState.Scanning or WorkflowState.Cleaning;
    public double ProgressValue
    {
        get => _progressValue;
        private set => this.RaiseAndSetIfChanged(ref _progressValue, value);
    }
    public string ProgressText
    {
        get => _progressText;
        private set => this.RaiseAndSetIfChanged(ref _progressText, value);
    }
    public int SelectedCount => Items.Count(item => item.IsSelected);
    public long SelectedBytes => Items.Where(item => item.IsSelected).Sum(item => item.Model.SizeBytes);
    public string SelectedSummary => SelectedCount == 0 ? "未选择" : $"已选 {SelectedCount} 项 · {ByteSizeFormatter.Format(SelectedBytes)}";
    public bool CanSelectAll => State == WorkflowState.Results && VisibleItems.Any(item => item.Model.CanClean);
    public bool IsAllSelected
    {
        get
        {
            var selectable = VisibleItems.Where(item => item.Model.CanClean).ToArray();
            return selectable.Length > 0 && selectable.All(item => item.IsSelected);
        }
        set
        {
            if (!CanSelectAll)
            {
                return;
            }

            foreach (var item in VisibleItems.Where(item => item.Model.CanClean))
            {
                item.IsSelected = value;
            }

            RefreshSelectionProperties();
        }
    }
    public bool IsEmptyVisible => State == WorkflowState.Idle;
    public bool IsScanningEmptyVisible => State == WorkflowState.Scanning && Items.Count == 0;
    public bool IsListVisible => Items.Count > 0;
    public bool IsConfirmationVisible => State == WorkflowState.Confirming;
    public bool HasRiskSelection => SelectedItems.Any(item => item.Model.Risk != CleanupRisk.Low);
    public string ConfirmationWarning => HasRiskSelection
        ? "包含中高风险项目，清理后可能无法恢复或需要重新下载，请确认。"
        : "清理会永久删除所选缓存内容，请确认后继续。";
    public string ConfirmationTotal => ByteSizeFormatter.Format(SelectedBytes);
    public bool IsToastVisible
    {
        get => _isToastVisible;
        private set => this.RaiseAndSetIfChanged(ref _isToastVisible, value);
    }
    public string ToastText => $"清理完成 · 释放 {ByteSizeFormatter.Format(_freedBytes)}";
    public int LogCount => Logs.Count;
    public string StatusText => State switch
    {
        WorkflowState.Idle => "SYSTEM READY · 等待指令",
        WorkflowState.Scanning => "SCANNING · 正在扫描 C 盘",
        WorkflowState.Results => "SCAN COMPLETE · 扫描完成",
        WorkflowState.Confirming => "AWAIT CONFIRM · 等待确认",
        WorkflowState.Cleaning => "CLEANING · 正在清理",
        _ => "TASK COMPLETE · 清理完成"
    };
    public IBrush StatusBrush => State switch
    {
        WorkflowState.Confirming => new SolidColorBrush(Color.Parse("#FBBF24")),
        WorkflowState.Scanning or WorkflowState.Cleaning => new SolidColorBrush(Color.Parse("#22D3EE")),
        _ => new SolidColorBrush(Color.Parse("#34D399"))
    };
    public string StateCode => $"CLEARC · v0.1.0 · STATE {(int)State + 1:00}/06";

    private async Task HandlePrimaryAsync()
    {
        if (State == WorkflowState.Idle)
        {
            await ScanAsync();
        }
        else if (State == WorkflowState.Results && SelectedCount > 0)
        {
            RebuildSelectedItems();
            State = WorkflowState.Confirming;
            AddLog("INFO", $"等待确认：{SelectedCount} 项 · {ByteSizeFormatter.Format(SelectedBytes)}");
        }
    }

    private async Task HandleSecondaryAsync()
    {
        if (State is WorkflowState.Scanning or WorkflowState.Cleaning)
        {
            _operationCancellation?.Cancel();
            return;
        }

        if (State is WorkflowState.Results or WorkflowState.Done)
        {
            await ScanAsync();
        }
    }

    private async Task ScanAsync()
    {
        _operationCancellation?.Dispose();
        _operationCancellation = new CancellationTokenSource();
        IsToastVisible = false;
        Items.Clear();
        VisibleItems.Clear();
        SelectedItems.Clear();
        UpdateFilterCounts();
        ProgressValue = 0;
        ProgressText = "准备扫描…";
        State = WorkflowState.Scanning;
        AddLog("INFO", "开始扫描 C 盘 …");

        var progress = new CallbackProgress<ScanProgress>(value =>
        {
            ProgressValue = value.Ratio * 100;
            var displayIndex = value.Completed >= value.Total ? value.Total : value.Completed + 1;
            ProgressText = $"{displayIndex:00}/{value.Total:00} · {value.CurrentTarget}";
            if (value.Completed < value.Total)
            {
                AddLog("INFO", $"[{value.Completed + 1:00}/{value.Total:00}] 扫描 {value.CurrentTarget}");
            }
        });

        try
        {
            var result = await _scanner.ScanAsync(progress, _operationCancellation.Token);
            _disk = result.Disk;
            var selection = new CleanupSelection(result.Items);
            foreach (var model in result.Items)
            {
                var item = new CleanupItemViewModel(model) { CanSelect = true };
                item.SetInitialSelection(selection.IsSelected(model.Id));
                item.SelectionChanged += OnItemSelectionChanged;
                Items.Add(item);
            }

            UpdateFilterCounts();
            RebuildVisibleItems();
            RebuildSelectedItems();
            State = WorkflowState.Results;
            AddLog("OK", $"扫描完成 · 耗时 {result.Elapsed.TotalSeconds:0.0}s · 定位 {result.Items.Sum(item => item.FileCount):N0} 个文件");
            AddLog("INFO", $"共 {result.Items.Count} 个目标位置 · 总占用 {ByteSizeFormatter.Format(result.TotalBytes)}");
            AddLog("INFO", $"默认选择低风险缓存 {SelectedCount} 项 · {ByteSizeFormatter.Format(SelectedBytes)}");
        }
        catch (OperationCanceledException)
        {
            State = WorkflowState.Idle;
            AddLog("WARN", "扫描已取消。");
        }
        catch (Exception exception)
        {
            State = WorkflowState.Idle;
            AddLog("ERR", $"扫描失败：{exception.Message}");
        }
        finally
        {
            RefreshStateProperties();
        }
    }

    private async Task ConfirmCleanupAsync()
    {
        var selectedIds = SelectedItems.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        IReadOnlyList<CleanupItem> plan;
        try
        {
            plan = _safetyPolicy.BuildPlan(
                Items.Select(item => item.Model),
                selectedIds,
                selectedIds);
        }
        catch (InvalidOperationException exception)
        {
            AddLog("ERR", exception.Message);
            State = WorkflowState.Results;
            return;
        }

        _operationCancellation?.Dispose();
        _operationCancellation = new CancellationTokenSource();
        _freedBytes = 0;
        ProgressValue = 0;
        ProgressText = "准备清理…";
        State = WorkflowState.Cleaning;
        AddLog("INFO", $"开始清理 {plan.Count} 项 · 预计释放 {ByteSizeFormatter.Format(plan.Sum(item => item.SizeBytes))}");

        var progress = new CallbackProgress<CleanupProgress>(value =>
        {
            ProgressValue = value.Ratio * 100;
            var displayIndex = value.Result is null ? value.Completed + 1 : value.Completed;
            ProgressText = $"{Math.Min(displayIndex, value.Total):00}/{value.Total:00} · {value.Item.DisplayName}";
            var row = Items.FirstOrDefault(item => item.Id == value.Item.Id);
            foreach (var item in Items)
            {
                item.IsCurrent = item == row && value.Result is null;
            }

            if (value.Result is null)
            {
                AddLog("INFO", $"[{value.Completed + 1:00}/{value.Total:00}] 清理 {value.Item.DisplayName} …");
            }
            else if (row is not null)
            {
                row.ApplyResult(value.Result);
                _freedBytes += value.Result.FreedBytes;
                this.RaisePropertyChanged(nameof(HeroValue));
                RefreshSelectionProperties();
                AddLog(value.Result.Outcome switch
                {
                    CleanupOutcome.Completed => "OK",
                    CleanupOutcome.Failed => "ERR",
                    _ => "WARN"
                }, $"{value.Item.DisplayName}：{value.Result.Message}");
            }
        });

        try
        {
            var result = await _executor.CleanAsync(plan, progress, _operationCancellation.Token);
            _freedBytes = result.FreedBytes;
            _disk = _disk with { FreeBytes = Math.Min(_disk.TotalBytes, _disk.FreeBytes + result.FreedBytes) };
            State = WorkflowState.Done;
            IsToastVisible = true;
            AddLog("OK", $"清理完成 · 成功 {result.CompletedCount} 项 · 释放 {ByteSizeFormatter.Format(result.FreedBytes)}");
            AddLog("INFO", $"{_disk.DriveName} 可用空间约为 {ByteSizeFormatter.Format(_disk.FreeBytes)}，建议重新分析获取精确值。");
        }
        catch (OperationCanceledException)
        {
            State = WorkflowState.Done;
            AddLog("WARN", "已停止后续清理任务；正在执行的官方命令已等待其安全结束。");
        }
        catch (Exception exception)
        {
            State = WorkflowState.Done;
            AddLog("ERR", $"清理任务失败：{exception.Message}");
        }
        finally
        {
            RebuildSelectedItems();
            RefreshStateProperties();
        }
    }

    private void CancelConfirmation()
    {
        if (State == WorkflowState.Confirming)
        {
            State = WorkflowState.Results;
            AddLog("INFO", "已取消清理确认。");
        }
    }

    private void SelectFilter(CategoryFilterViewModel filter)
    {
        _activeFilter = filter;
        foreach (var item in Filters)
        {
            item.IsActive = item == filter;
        }

        RebuildVisibleItems();
    }

    private void OnItemSelectionChanged(object? sender, EventArgs e)
    {
        RebuildSelectedItems();
        RefreshSelectionProperties();
    }

    private void RebuildVisibleItems()
    {
        VisibleItems.Clear();
        foreach (var item in Items.Where(item => _activeFilter?.Category is null || item.Category == _activeFilter.Category))
        {
            VisibleItems.Add(item);
        }

        this.RaisePropertyChanged(nameof(IsAllSelected));
        this.RaisePropertyChanged(nameof(CanSelectAll));
        this.RaisePropertyChanged(nameof(IsListVisible));
    }

    private void RebuildSelectedItems()
    {
        SelectedItems.Clear();
        foreach (var item in Items.Where(item => item.IsSelected))
        {
            SelectedItems.Add(item);
        }

        this.RaisePropertyChanged(nameof(HasRiskSelection));
        this.RaisePropertyChanged(nameof(ConfirmationWarning));
        this.RaisePropertyChanged(nameof(ConfirmationTotal));
    }

    private void UpdateFilterCounts()
    {
        foreach (var filter in Filters)
        {
            filter.Count = Items.Count(item => filter.Category is null || item.Category == filter.Category);
        }
    }

    private void ClearLogs()
    {
        Logs.Clear();
        this.RaisePropertyChanged(nameof(LogCount));
    }

    private void AddLog(string level, string message)
    {
        Logs.Add(LogEntryViewModel.Create(level, message));
        this.RaisePropertyChanged(nameof(LogCount));
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
    }

    private void RefreshStateProperties()
    {
        this.RaisePropertyChanged(nameof(DriveTitle));
        this.RaisePropertyChanged(nameof(DriveInfo));
        this.RaisePropertyChanged(nameof(UsedRatio));
        this.RaisePropertyChanged(nameof(UsedPercent));
        this.RaisePropertyChanged(nameof(DiskUsage));
        this.RaisePropertyChanged(nameof(HeroLabel));
        this.RaisePropertyChanged(nameof(HeroValue));
        this.RaisePropertyChanged(nameof(IsHeroPlaceholder));
        this.RaisePropertyChanged(nameof(PrimaryButtonText));
        this.RaisePropertyChanged(nameof(IsPrimaryEnabled));
        this.RaisePropertyChanged(nameof(SecondaryButtonText));
        this.RaisePropertyChanged(nameof(IsSecondaryEnabled));
        this.RaisePropertyChanged(nameof(IsProgressVisible));
        this.RaisePropertyChanged(nameof(CanSelectAll));
        this.RaisePropertyChanged(nameof(IsEmptyVisible));
        this.RaisePropertyChanged(nameof(IsScanningEmptyVisible));
        this.RaisePropertyChanged(nameof(IsListVisible));
        this.RaisePropertyChanged(nameof(IsConfirmationVisible));
        this.RaisePropertyChanged(nameof(StatusText));
        this.RaisePropertyChanged(nameof(StatusBrush));
        this.RaisePropertyChanged(nameof(StateCode));
        this.RaisePropertyChanged(nameof(ToastText));
        RefreshSelectionProperties();
    }

    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
