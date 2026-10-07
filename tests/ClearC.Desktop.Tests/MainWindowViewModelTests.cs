using ClearC.Core.Models;
using ClearC.Core.Safety;
using ClearC.Core.Services;
using ClearC.Desktop.Infrastructure.Logging;
using ClearC.Desktop.Infrastructure.Windows;
using ClearC.Desktop.ViewModels;

namespace ClearC.Desktop.Tests;

/// <summary>
/// 视图模型把扫描/清理进度投递到 UI 线程，因此这里和视图测试共用同一个 headless UI 会话。
/// </summary>
[Collection(AvaloniaHeadlessFixture.CollectionName)]
public sealed class MainWindowViewModelTests(AvaloniaHeadlessFixture fixture)
{
    [Fact]
    public Task Scan_SelectsOnlyLowRiskCleanableItems() => RunAsync(async () =>
    {
        var viewModel = CreateViewModel();

        viewModel.PrimaryCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Results);

        Assert.Equal(WorkflowState.Results, viewModel.State);
        Assert.Equal(1, viewModel.SelectedCount);
        Assert.Equal(1024, viewModel.SelectedBytes);
        Assert.True(Row(viewModel, "low").IsSelected);
        Assert.False(Row(viewModel, "medium").IsSelected);
        var codex = Row(viewModel, "codex-data");
        Assert.False(codex.IsSelected);
        Assert.True(codex.CanSelect);
        Assert.Contains("不连接 Codex", codex.SelectionToolTip);
    });

    [Fact]
    public Task Scan_GroupsRowsByDisplayGroupAndSortsBySize() => RunAsync(async () =>
    {
        var viewModel = CreateViewModel();

        viewModel.PrimaryCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Results);

        // 分区只有一个（C: 系统盘）；组间按小计降序：用户文件 4096 > 系统缓存 2048 > 临时文件 1024。
        var section = Assert.Single(viewModel.Sections);
        Assert.Equal("C:", section.DriveName);
        Assert.Equal("系统盘", section.TypeText);
        Assert.Equal(
            new[]
            {
                CleanupDisplayGroup.UserFiles,
                CleanupDisplayGroup.SystemCache,
                CleanupDisplayGroup.TemporaryFiles
            },
            section.Groups.Select(group => group.Group));
        Assert.Equal(4096, section.Groups[0].SubtotalBytes);
        Assert.Equal(["codex-data"], section.Groups[0].Items.Select(item => item.Id));
        // 默认全部折叠（手风琴），展开一个即折叠其余。
        Assert.All(section.Groups, group => Assert.False(group.IsExpanded));
        Assert.All(section.Groups, group => Assert.Equal(-90, group.ChevronRotation));

        section.Groups[1].IsExpanded = true;

        Assert.True(section.Groups[1].IsExpanded);
        Assert.All(
            section.Groups.Where(group => !ReferenceEquals(group, section.Groups[1])),
            group => Assert.False(group.IsExpanded));
        Assert.Equal("系统缓存", section.Groups[1].Name);
        Assert.Equal("1 项", section.Groups[1].CountText);
    });

    [Fact]
    public Task Scan_HidesZeroByteRowsUntilTheToggleIsSwitchedOff() => RunAsync(async () =>
    {
        var viewModel = CreateViewModel();

        viewModel.PrimaryCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Results);

        Assert.DoesNotContain(VisibleRows(viewModel), item => item.Id == "empty");
        Assert.Contains(viewModel.Items, item => item.Id == "empty");

        viewModel.HideZeroByteItems = false;

        Assert.Contains(VisibleRows(viewModel), item => item.Id == "empty");
    });

    /// <summary>全选只作用于当前可见且可清理的行（§5.6），被「隐藏 0 字节项」过滤掉的行不参与。</summary>
    [Fact]
    public Task SelectAll_OnlyTouchesVisibleCleanableRows() => RunAsync(async () =>
    {
        var viewModel = CreateViewModel();
        viewModel.PrimaryCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Results);

        viewModel.IsAllSelected = true;

        Assert.True(viewModel.IsAllSelected);
        Assert.True(Row(viewModel, "low").IsSelected);
        Assert.True(Row(viewModel, "medium").IsSelected);
        Assert.True(Row(viewModel, "codex-data").IsSelected);
        Assert.False(Row(viewModel, "empty").IsSelected);
        Assert.Equal(3, viewModel.SelectedCount);

        viewModel.IsAllSelected = false;

        Assert.False(viewModel.IsAllSelected);
        Assert.Equal(0, viewModel.SelectedCount);
    });

    /// <summary>详情里的"打开位置"用第一个清理根，失败原因回显在详情里。</summary>
    [Fact]
    public Task RowDetail_OpensTheFirstCleanRootAndReportsFailures() => RunAsync(async () =>
    {
        var opener = new FakeLocationOpener("路径已不存在，可能已被清理");
        var viewModel = CreateViewModel(locationOpener: opener);
        viewModel.PrimaryCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Results);
        var row = Row(viewModel, "low");

        Assert.False(row.HasLocationHint);

        row.OpenLocationCommand.Execute(null);

        Assert.Equal(@"C:\Temp", opener.LastPath);
        Assert.True(row.HasLocationHint);
        Assert.Equal("路径已不存在，可能已被清理", row.LocationHint);
    });

    [Fact]
    public Task RowDetail_KeepsTheHintEmptyWhenTheLocationOpened() => RunAsync(async () =>
    {
        var opener = new FakeLocationOpener(null);
        var viewModel = CreateViewModel(locationOpener: opener);
        viewModel.PrimaryCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Results);

        Row(viewModel, "low").OpenLocationCommand.Execute(null);

        Assert.Equal(@"C:\Temp", opener.LastPath);
        Assert.False(Row(viewModel, "low").HasLocationHint);
    });

    /// <summary>Esc 关闭当前模态：确认页回到结果页，关闭保护直接收起。</summary>
    [Fact]
    public Task Escape_DismissesTheCurrentModal() => RunAsync(async () =>
    {
        var viewModel = CreateViewModel();
        viewModel.PrimaryCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Results);

        Assert.False(viewModel.TryDismissModal());

        viewModel.PrimaryCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Confirming);

        Assert.True(viewModel.TryDismissModal());
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Results);
        Assert.False(viewModel.IsConfirmationVisible);

        viewModel.RequestCloseConfirmation();

        Assert.True(viewModel.TryDismissModal());
        Assert.False(viewModel.IsCloseConfirmationVisible);
    });

    /// <summary>快速模式默认开：DISM / vssadmin 分析项默认不扫，取消勾选后重扫才带上它们。</summary>
    [Fact]
    public Task FastMode_SkipsSystemAnalysisByDefault() => RunAsync(async () =>
    {
        var scanner = new FakeScanner();
        var viewModel = CreateViewModel(scanner);

        Assert.True(viewModel.SkipSystemAnalysis);

        viewModel.PrimaryCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Results);

        Assert.True(scanner.LastSkipSystemAnalysis);

        viewModel.SkipSystemAnalysis = false;
        viewModel.SecondaryCommand.Execute(null);
        await WaitUntilAsync(() => scanner.LastSkipSystemAnalysis == false && viewModel.State == WorkflowState.Results);

        Assert.False(scanner.LastSkipSystemAnalysis);
    });

    [Fact]
    public Task Cleanup_TransitionsThroughConfirmationAndDone() => RunAsync(async () =>
    {
        var viewModel = CreateViewModel();
        viewModel.PrimaryCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Results);

        viewModel.PrimaryCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Confirming);
        Assert.True(viewModel.IsConfirmationVisible);
        Assert.True(viewModel.IsConfirmEnabled);

        viewModel.ConfirmCleanupCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Done);

        Assert.True(viewModel.IsToastVisible);
        Assert.Contains("1.0 KB", viewModel.ToastText);
        Assert.Equal("✓ 已清理", Row(viewModel, "low").ResultText);
        Assert.Equal("释放 1.0 KB", Row(viewModel, "low").FreedText);
    });

    [Fact]
    public Task Cleanup_SeparatesMeasuredFromEstimatedReleaseAmounts() => RunAsync(async () =>
    {
        var viewModel = CreateViewModel();
        viewModel.PrimaryCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Results);
        Row(viewModel, "medium").IsSelected = true;

        viewModel.PrimaryCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Confirming);
        viewModel.ConfirmCleanupCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Done);

        Assert.Contains("~", Row(viewModel, "medium").FreedText);
        Assert.Contains("（估算）", Row(viewModel, "medium").FreedText);
        Assert.Contains("实测释放 1.0 KB", viewModel.ToastText);
        Assert.Contains("预计再释放 ~2.0 KB", viewModel.ToastText);
    });

    [Fact]
    public Task Cleanup_MarksTheRowCurrentlyBeingCleaned() => RunAsync(async () =>
    {
        var executor = new SlowExecutor();
        var viewModel = CreateViewModel(executor: executor);
        viewModel.PrimaryCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Results);
        viewModel.PrimaryCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Confirming);
        viewModel.ConfirmCleanupCommand.Execute(null);
        await WaitUntilAsync(() => Row(viewModel, "low").IsCurrent);

        Assert.Contains("01/01", viewModel.ProgressText);
        Assert.Contains("Low", viewModel.ProgressText);
        Assert.True(viewModel.IsProgressVisible);

        executor.Release();
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Done);
    });

    /// <summary>清空回收站走 Shell API，进度文案必须写明无法中断（§3.1）。</summary>
    [Fact]
    public Task Cleanup_NotesThatEmptyingTheRecycleBinCannotBeInterrupted() => RunAsync(async () =>
    {
        var executor = new SlowExecutor();
        var viewModel = CreateViewModel(new RecycleBinScanner(), executor);
        viewModel.PrimaryCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Results);
        Row(viewModel, "recycle-bin").IsSelected = true;
        viewModel.PrimaryCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Confirming);
        viewModel.ConfirmCleanupCommand.Execute(null);
        await WaitUntilAsync(() => Row(viewModel, "recycle-bin").IsCurrent);

        Assert.Contains("01/01", viewModel.ProgressText);
        Assert.Contains("清空过程无法中断", viewModel.ProgressText);

        executor.Release();
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Done);
    });

    [Fact]
    public Task MediumRiskSelection_IsCalledOutInConfirmation() => RunAsync(async () =>
    {
        var viewModel = CreateViewModel();
        viewModel.PrimaryCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Results);
        Row(viewModel, "medium").IsSelected = true;

        viewModel.PrimaryCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Confirming);

        Assert.True(viewModel.HasRiskSelection);
        Assert.Contains("中高风险", viewModel.ConfirmationWarning);
    });

    [Fact]
    public Task CodexSelection_RequiresClosingCodexInConfirmation() => RunAsync(async () =>
    {
        var viewModel = CreateViewModel();
        viewModel.PrimaryCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Results);
        Row(viewModel, "codex-data").IsSelected = true;

        viewModel.PrimaryCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Confirming);

        Assert.True(viewModel.HasCodexConversationSelection);
        Assert.Contains("请先关闭 Codex", viewModel.ConfirmationWarning);
    });

    [Fact]
    public Task DeniedItems_DisableConfirmationAndListTheirReasons() => RunAsync(async () =>
    {
        var viewModel = CreateViewModel(new DeniedScanner());
        viewModel.PrimaryCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Results);
        Row(viewModel, "protected").IsSelected = true;

        viewModel.PrimaryCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Confirming);

        var denied = Row(viewModel, "protected");
        Assert.True(denied.IsDenied);
        Assert.Contains("Documents", denied.DeniedReason);
        Assert.False(viewModel.IsConfirmEnabled);

        viewModel.ClearDeniedCommand.Execute(null);

        Assert.False(denied.IsDenied);
        Assert.False(denied.IsSelected);
        Assert.True(viewModel.IsConfirmEnabled);
    });

    [Fact]
    public Task CancelConfirmation_ReturnsToResultsAndClearsDenials() => RunAsync(async () =>
    {
        var viewModel = CreateViewModel(new DeniedScanner());
        viewModel.PrimaryCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Results);
        Row(viewModel, "protected").IsSelected = true;
        viewModel.PrimaryCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Confirming);

        viewModel.CancelConfirmationCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Results);

        Assert.False(Row(viewModel, "protected").IsDenied);
        Assert.False(viewModel.IsConfirmationVisible);
    });

    [Fact]
    public Task CancelScan_KeepsTheAlreadyDeliveredRows() => RunAsync(async () =>
    {
        var scanner = new BlockingScanner();
        var viewModel = CreateViewModel(scanner);
        viewModel.PrimaryCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.IsGhostVisible);

        Assert.Contains("正在扫描", viewModel.GhostText);

        viewModel.SecondaryCommand.Execute(null);

        Assert.True(viewModel.IsAwaitingCommandStop);
        Assert.Contains("正在等待当前命令结束", viewModel.SecondaryButtonText);
        Assert.False(viewModel.IsSecondaryEnabled);

        scanner.Release();
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Idle);

        // 已完成的行保留在列表里，幽灵行清空；重新扫描后才会再次可勾选。
        Assert.Contains(viewModel.Items, item => item.Id == "low");
        Assert.False(viewModel.IsGhostVisible);
        Assert.False(Row(viewModel, "low").CanSelect);
    });

    [Fact]
    public Task Toast_IsClearedByTheSchedulerAfterSixSeconds() => RunAsync(async () =>
    {
        var scheduler = new FakeToastScheduler();
        var viewModel = CreateViewModel(toastScheduler: scheduler);
        await CleanAndFinishAsync(viewModel);

        Assert.Equal(TimeSpan.FromSeconds(6), MainWindowViewModel.ToastDuration);
        Assert.Equal(MainWindowViewModel.ToastDuration, scheduler.Duration);

        scheduler.Fire();

        Assert.False(viewModel.IsToastVisible);
    });

    [Fact]
    public Task StopToastTimer_ClearsTheToastAndThePendingSchedule() => RunAsync(async () =>
    {
        var scheduler = new FakeToastScheduler();
        var viewModel = CreateViewModel(toastScheduler: scheduler);
        await CleanAndFinishAsync(viewModel);

        viewModel.StopToastTimer();

        Assert.False(viewModel.IsToastVisible);
        Assert.True(scheduler.Cancelled);
        Assert.False(scheduler.Fire());
    });

    [Fact]
    public Task ConfirmClose_RaisesCloseRequestedOnceTheOperationStopped() => RunAsync(async () =>
    {
        var viewModel = CreateViewModel();
        viewModel.PrimaryCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Results);
        var raised = 0;
        viewModel.CloseRequested += (_, _) => raised++;

        viewModel.RequestCloseConfirmation();
        viewModel.ConfirmCloseCommand.Execute(null);
        await WaitUntilAsync(() => raised > 0);

        Assert.Equal(1, raised);
        Assert.False(viewModel.IsCloseConfirmationVisible);
    });

    [Fact]
    public Task Rescan_ReplacesThePreviousResults() => RunAsync(async () =>
    {
        var viewModel = CreateViewModel();
        viewModel.PrimaryCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Results);

        viewModel.SecondaryCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Results && viewModel.Items.Count == 4);

        Assert.Equal(4, viewModel.Items.Count);
    });

    [Fact]
    public Task CloseGuard_AsksForConfirmationWhileScanningOrCleaning() => RunAsync(async () =>
    {
        var viewModel = CreateViewModel();
        viewModel.PrimaryCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Results);

        Assert.False(CloseGuard.RequiresConfirmation(viewModel.State));
        Assert.False(viewModel.IsCloseConfirmationVisible);

        viewModel.RequestCloseConfirmation();

        Assert.True(viewModel.IsCloseConfirmationVisible);
        Assert.False(viewModel.IsConfirmationVisible);
        Assert.True(CloseGuard.RequiresConfirmation(WorkflowState.Scanning));
        Assert.True(CloseGuard.RequiresConfirmation(WorkflowState.Cleaning));
        Assert.True(CloseGuard.GracefulStopTimeout > TimeSpan.Zero);
    });

    [Fact]
    public Task ElevationBanner_IsVisibleWhileNotElevated() => Run(() =>
    {
        var viewModel = CreateViewModel(elevationService: new FakeElevationService(false));

        Assert.True(viewModel.IsElevationBannerVisible);
        Assert.Equal("未提权 · 部分系统缓存不可清理", viewModel.ElevationBannerText);
    });

    [Fact]
    public Task StateCode_ReportsTheSixStateMachinePosition() => Run(() =>
    {
        var viewModel = CreateViewModel();

        Assert.Equal(WorkflowState.Idle, viewModel.State);
        Assert.EndsWith("STATE 01/06", viewModel.StateCode);
        Assert.Equal("扫描分析", viewModel.PrimaryButtonText);
        Assert.True(viewModel.IsPrimaryGlowing);
        Assert.Equal("i-search", viewModel.PrimaryIconKey);
    });

    [Fact]
    public Task RestartElevated_AsksTheElevationServiceAndClosesTheCurrentInstance() => Run(() =>
    {
        var elevation = new FakeElevationService(false, restartSucceeds: true);
        var viewModel = CreateViewModel(elevationService: elevation);
        var closed = 0;
        viewModel.CloseRequested += (_, _) => closed++;

        viewModel.RestartElevatedCommand.Execute(null);

        Assert.Equal(1, elevation.RestartCount);
        Assert.Equal(1, closed);
    });

    private async Task CleanAndFinishAsync(MainWindowViewModel viewModel)
    {
        viewModel.PrimaryCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Results);
        viewModel.PrimaryCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Confirming);
        viewModel.ConfirmCleanupCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Done);
    }

    private Task RunAsync(Func<Task> body) =>
        fixture.Session.Dispatch(body, TestContext.Current.CancellationToken);

    private Task Run(Action body) =>
        fixture.Session.Dispatch(body, TestContext.Current.CancellationToken);

    private static CleanupItemViewModel Row(MainWindowViewModel viewModel, string id) =>
        viewModel.Items.Single(item => item.Id == id);

    private static IReadOnlyList<CleanupItemViewModel> VisibleRows(MainWindowViewModel viewModel) =>
        viewModel.Sections
            .SelectMany(section => section.Groups)
            .SelectMany(group => group.Items)
            .ToArray();

    private static MainWindowViewModel CreateViewModel(
        ICleanupScanner? scanner = null,
        ICleanupExecutor? executor = null,
        IElevationService? elevationService = null,
        IToastScheduler? toastScheduler = null,
        ILocationOpener? locationOpener = null,
        IReadOnlyList<DiskSnapshot>? initialDrives = null) => new(
        scanner ?? new FakeScanner(),
        executor ?? new FakeExecutor(),
        new CleanupSafetyPolicy(),
        new DiskSnapshot("C:", "NTFS", 100_000, 40_000),
        new InMemoryLogStore(NullApplicationLogger.Instance),
        elevationService ?? new FakeElevationService(true),
        toastScheduler: toastScheduler,
        locationOpener: locationOpener,
        initialDrives: initialDrives);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(10, cancellationToken);
        }

        Assert.True(condition(), "The view model did not reach the expected state.");
    }

    private sealed class FakeScanner : ICleanupScanner
    {
        /// <summary>视图模型传给扫描器的"快速模式"开关，用来断言默认值与切换。</summary>
        public bool? LastSkipSystemAnalysis { get; private set; }

        /// <summary>视图模型传给扫描器的盘符范围。</summary>
        public IReadOnlyList<string>? LastDriveScope { get; private set; }

        public Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default, bool skipSystemAnalysis = false, IReadOnlyList<string>? driveScope = null)
        {
            LastSkipSystemAnalysis = skipSystemAnalysis;
            LastDriveScope = driveScope;
            CleanupItem[] items =
            [
                new("low", "Low", @"C:\Temp", CleanupCategory.TemporaryFiles, CleanupRisk.Low, 1024, 2, "", "low",
                    CleanerKind: CleanerKind.DirectoryContents, Paths: [@"C:\Temp"]),
                new("medium", "Medium", @"C:\Cache", CleanupCategory.PackageCache, CleanupRisk.Medium, 2048, 3, "", "medium",
                    CleanerKind: CleanerKind.Command, Paths: [@"C:\Cache"]),
                new("codex-data", "Codex 会话记录", @"C:\Users\test\.codex", CleanupCategory.ApplicationData, CleanupRisk.High, 4096, 4, "", "codex-conversations",
                    CleanerKind: CleanerKind.CodexConversations, Paths: [@"C:\Users\test\.codex\sessions"]),
                new("empty", "Empty", @"C:\Empty", CleanupCategory.SystemLogs, CleanupRisk.Low, 0, 0, "", "empty",
                    CleanerKind: CleanerKind.DirectoryContents, Paths: [@"C:\Empty"])
            ];
            for (var index = 0; index < items.Length; index++)
            {
                progress?.Report(new(index, items.Length, items[index].DisplayName));
                progress?.Report(new(index + 1, items.Length, items[index].DisplayName, ScanTier.Fast, items[index]));
            }

            return Task.FromResult(new ScanResult(new("C:", "NTFS", 100_000, 40_000), items, TimeSpan.FromSeconds(1)));
        }
    }

    /// <summary>C 盘与 D 盘各产出一条目标，用来验证分盘默认勾选策略与分区。</summary>
    private sealed class DualDriveScanner : ICleanupScanner
    {
        public IReadOnlyList<string>? LastDriveScope { get; private set; }

        public Task<ScanResult> ScanAsync(
            IProgress<ScanProgress>? progress = null,
            CancellationToken cancellationToken = default,
            bool skipSystemAnalysis = false,
            IReadOnlyList<string>? driveScope = null)
        {
            LastDriveScope = driveScope;
            CleanupItem[] items =
            [
                new("low", "Low", @"C:\Temp", CleanupCategory.TemporaryFiles, CleanupRisk.Low, 1024, 2, "", "low",
                    CleanerKind: CleanerKind.DirectoryContents, Paths: [@"C:\Temp"]),
                new("d-temp", "D 盘临时目录", @"D:\Temp", CleanupCategory.TemporaryFiles, CleanupRisk.Low, 2048, 4, "", "d-temp",
                    CleanerKind: CleanerKind.DirectoryContents, Paths: [@"D:\Temp"])
            ];
            return Task.FromResult(new ScanResult(new("C:", "NTFS", 100_000, 40_000), items, TimeSpan.FromSeconds(1)));
        }
    }

    /// <summary>报完已完成的快档行后把幽灵行停在第二个目标上，用来验证取消与幽灵行。</summary>
    private sealed class BlockingScanner : ICleanupScanner
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _gate.TrySetResult();

        public async Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default, bool skipSystemAnalysis = false, IReadOnlyList<string>? driveScope = null)
        {
            CleanupItem[] items =
            [
                new("low", "Low", @"C:\Temp", CleanupCategory.TemporaryFiles, CleanupRisk.Low, 1024, 2, "", "low",
                    CleanerKind: CleanerKind.DirectoryContents, Paths: [@"C:\Temp"]),
                new("slow", "Slow", @"C:\Slow", CleanupCategory.PackageCache, CleanupRisk.Low, 2048, 3, "", "slow",
                    CleanerKind: CleanerKind.DirectoryContents, Paths: [@"C:\Slow"])
            ];
            progress?.Report(new(0, 3, items[0].DisplayName));
            progress?.Report(new(1, 3, items[0].DisplayName, ScanTier.Fast, items[0]));
            progress?.Report(new(1, 3, items[1].DisplayName, ScanTier.Slow));
            await _gate.Task.WaitAsync(cancellationToken);
            progress?.Report(new(2, 3, items[1].DisplayName, ScanTier.Slow, items[1]));
            return new ScanResult(new("C:", "NTFS", 100_000, 40_000), items, TimeSpan.FromSeconds(1));
        }
    }

    /// <summary>包含一个会被安全策略拒绝的受保护路径。</summary>
    private sealed class DeniedScanner : ICleanupScanner
    {
        public Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default, bool skipSystemAnalysis = false, IReadOnlyList<string>? driveScope = null)
        {
            CleanupItem[] items =
            [
                new("low", "Low", @"C:\Temp", CleanupCategory.TemporaryFiles, CleanupRisk.Low, 1024, 2, "", "low",
                    CleanerKind: CleanerKind.DirectoryContents, Paths: [@"C:\Temp"]),
                new("protected", "Protected", @"C:\Users\test\Documents", CleanupCategory.ApplicationData, CleanupRisk.Low, 2048, 5, "", "protected",
                    CleanerKind: CleanerKind.DirectoryContents, Paths: [@"C:\Users\test\Documents"])
            ];
            return Task.FromResult(new ScanResult(new("C:", "NTFS", 100_000, 40_000), items, TimeSpan.FromSeconds(1)));
        }
    }

    /// <summary>只产出回收站一行，用来验证"清空过程无法中断"的进度文案。</summary>
    private sealed class RecycleBinScanner : ICleanupScanner
    {
        public Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default, bool skipSystemAnalysis = false, IReadOnlyList<string>? driveScope = null)
        {
            CleanupItem[] items =
            [
                new("recycle-bin", "回收站", @"C:\$Recycle.Bin", CleanupCategory.RecycleBin, CleanupRisk.Medium, 4096, 6,
                    "清空后文件无法从回收站恢复，执行前必须单独确认。", "recycle-bin",
                    CleanerKind: CleanerKind.RecycleBin, Paths: [@"C:\$Recycle.Bin"], Icon: "i-trash", Accent: "#64748b")
            ];
            progress?.Report(new(0, 2, items[0].DisplayName));
            progress?.Report(new(1, 2, items[0].DisplayName, ScanTier.Fast, items[0]));
            return Task.FromResult(new ScanResult(new("C:", "NTFS", 100_000, 40_000), items, TimeSpan.FromSeconds(1)));
        }
    }

    private sealed class FakeExecutor : ICleanupExecutor
    {
        public Task<CleanupResult> CleanAsync(IReadOnlyList<CleanupItem> plan, IProgress<CleanupProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            var results = new List<CleanupItemResult>();
            for (var index = 0; index < plan.Count; index++)
            {
                var item = plan[index];
                progress?.Report(new(index, plan.Count, item));
                // 官方命令类目标只能估算（§2.1）。
                var result = new CleanupItemResult(
                    item.Id,
                    CleanupOutcome.Completed,
                    item.SizeBytes,
                    "completed",
                    IsEstimated: item.CleanerKind == CleanerKind.Command);
                results.Add(result);
                progress?.Report(new(index + 1, plan.Count, item, result));
            }

            return Task.FromResult(new CleanupResult(results, TimeSpan.FromSeconds(1)));
        }
    }

    /// <summary>在第一项上报"正在清理"后挂起，用来验证当前行高亮与进度文案。</summary>
    private sealed class SlowExecutor : ICleanupExecutor
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _gate.TrySetResult();

        public async Task<CleanupResult> CleanAsync(
            IReadOnlyList<CleanupItem> plan,
            IProgress<CleanupProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            progress?.Report(new(0, plan.Count, plan[0]));
            await _gate.Task;
            var results = plan
                .Select(item => new CleanupItemResult(item.Id, CleanupOutcome.Completed, item.SizeBytes, "completed"))
                .ToArray();
            progress?.Report(new(plan.Count, plan.Count, plan[0], results[0]));
            return new CleanupResult(results, TimeSpan.FromSeconds(1));
        }
    }

    private sealed class FakeToastScheduler : IToastScheduler
    {
        private Action? _callback;

        public TimeSpan? Duration { get; private set; }
        public bool Cancelled { get; private set; }

        public void Schedule(TimeSpan delay, Action callback)
        {
            Duration = delay;
            Cancelled = false;
            _callback = callback;
        }

        public void Cancel()
        {
            Cancelled = true;
            _callback = null;
        }

        /// <summary>模拟定时器到点；已被取消时返回 false。</summary>
        public bool Fire()
        {
            if (_callback is not { } callback)
            {
                return false;
            }

            _callback = null;
            callback();
            return true;
        }
    }

    /// <summary>记录最后一次请求的路径，并返回固定的失败原因（<c>null</c> 表示打开成功）。</summary>
    private sealed class FakeLocationOpener(string? reason) : ILocationOpener
    {
        public string? LastPath { get; private set; }

        public string? TryOpen(string path)
        {
            LastPath = path;
            return reason;
        }
    }

    private sealed class FakeElevationService(bool isElevated, bool restartSucceeds = false) : IElevationService
    {
        public int RestartCount { get; private set; }

        public bool IsElevated { get; } = isElevated;

        public bool RestartElevated()
        {
            RestartCount++;
            return restartSucceeds;
        }
    }
}
