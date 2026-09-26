using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using ClearC.Core.Models;
using ClearC.Core.Safety;
using ClearC.Core.Services;
using ClearC.Desktop.Controls;
using ClearC.Desktop.Infrastructure.Logging;
using ClearC.Desktop.Infrastructure.Windows;
using ClearC.Desktop.ViewModels;
using ClearC.Desktop.Views;

namespace ClearC.Desktop.Tests;

[Collection(AvaloniaHeadlessFixture.CollectionName)]
public sealed class MainWindowHeadlessTests(AvaloniaHeadlessFixture fixture)
{
    [Fact]
    public async Task MainWindow_MatchesPrototypeDimensionsAndLoadsCoreControls()
    {
        await fixture.Session.Dispatch(() =>
        {
            var window = CreateWindow(new EmptyScanner());
            window.Show();

            Assert.Equal(1060, window.Width);
            Assert.Equal(700, window.Height);
            Assert.Single(window.GetVisualDescendants().OfType<DiskDonut>());
            Assert.Single(window.GetVisualDescendants().OfType<LogPanelView>());
            Assert.Single(window.GetVisualDescendants().OfType<CleanupListView>());
            Assert.IsType<TitleBarViewModel>(Assert.Single(window.GetVisualDescendants().OfType<TitleBarView>()).DataContext);
            Assert.IsType<CleanupWorkspaceViewModel>(Assert.Single(window.GetVisualDescendants().OfType<CleanupWorkspaceView>()).DataContext);
            Assert.IsType<LogPanelViewModel>(Assert.Single(window.GetVisualDescendants().OfType<LogPanelView>()).DataContext);
            Assert.IsType<StatusBarViewModel>(Assert.Single(window.GetVisualDescendants().OfType<StatusBarView>()).DataContext);
            Assert.IsType<WorkflowOverlayViewModel>(Assert.Single(window.GetVisualDescendants().OfType<WorkflowOverlayView>()).DataContext);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Shell_UsesThePrototypeRowHeightsAndStatusBar()
    {
        await fixture.Session.Dispatch(() =>
        {
            var window = CreateWindow(new EmptyScanner());
            window.Show();
            window.UpdateLayout();

            var root = Assert.IsType<Border>(window.Content);
            var grid = Assert.IsType<Grid>(root.Child);
            Assert.Equal(new GridLength(44), grid.RowDefinitions[0].Height);
            Assert.Equal(new GridLength(160), grid.RowDefinitions[2].Height);
            Assert.Equal(new GridLength(28), grid.RowDefinitions[3].Height);

            var statusBar = Assert.Single(window.GetVisualDescendants().OfType<StatusBarView>());
            Assert.Equal(28, statusBar.Bounds.Height);
            Assert.Contains(
                statusBar.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text?.Contains("SYSTEM READY", StringComparison.Ordinal) == true);

            // 按钮高 36（§6.8）；模态里的主按钮此时不可见，只量渲染出来的那些。
            var buttons = window.GetVisualDescendants()
                .OfType<Button>()
                .Where(button => button.Classes.Contains("btn") && button.IsEffectivelyVisible)
                .ToArray();
            Assert.NotEmpty(buttons);
            Assert.All(buttons, button => Assert.Equal(36, button.Bounds.Height));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CleanupRows_UseThePrototypeColumnWidthsAndAlignWithTheirGroup()
    {
        await fixture.Session.Dispatch(async () =>
        {
            var viewModel = CreateViewModel(new AlignmentScanner());
            var window = CreateWindow(viewModel);
            window.Show();

            viewModel.PrimaryCommand.Execute(null);
            for (var attempt = 0; attempt < 200 && viewModel.State != WorkflowState.Results; attempt++)
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }

            Assert.Equal(WorkflowState.Results, viewModel.State);
            viewModel.Groups[0].IsExpanded = true;
            window.UpdateLayout();

            var rows = window.GetVisualDescendants()
                .OfType<ToggleButton>()
                .Where(row => row.Classes.Contains("rowMain"))
                .ToArray();
            Assert.Equal(3, rows.Length);

            var leftEdges = rows.Select(row => row.TranslatePoint(default, window)!.Value.X).ToArray();
            Assert.Single(leftEdges.Distinct());
            Assert.Single(rows.Select(row => row.Bounds.Width).Distinct());

            // 固定列宽：元信息 158 / 大小 92 / 状态 64（§6.5）。
            var rowGrid = Assert.IsType<Grid>(rows[0].Content);
            Assert.Equal(
                "22,34,12,*,Auto,158,92,64,14",
                string.Join(',', rowGrid.ColumnDefinitions.Select(column => column.Width.ToString())));
            Assert.Equal(new GridLength(158), rowGrid.ColumnDefinitions[5].Width);
            Assert.Equal(new GridLength(92), rowGrid.ColumnDefinitions[6].Width);
            Assert.Equal(new GridLength(64), rowGrid.ColumnDefinitions[7].Width);

            // 行 tile 34（§6.8）。
            var tile = rowGrid.Children
                .OfType<Border>()
                .Single(border => border.Width == 34 && border.Height == 34);
            Assert.Equal(34, tile.Bounds.Height);

            // 组头与行同宽（都贴住列表左右边界）。
            var groupHead = window.GetVisualDescendants()
                .OfType<ToggleButton>()
                .Single(head => head.Classes.Contains("ghead"));
            Assert.Equal(rows[0].Bounds.Width, groupHead.Bounds.Width, 1);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ElevationBanner_RendersTheRestartButtonWhileNotElevated()
    {
        await fixture.Session.Dispatch(() =>
        {
            var window = CreateWindow(new EmptyScanner(), isElevated: false);
            window.Show();
            window.UpdateLayout();

            var banner = Assert.Single(
                window.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "未提权 · 部分系统缓存不可清理");
            Assert.True(banner.IsEffectivelyVisible);

            var restart = Assert.Single(
                window.GetVisualDescendants().OfType<Button>(),
                button => Equals(button.Content, "以管理员重启"));
            Assert.True(restart.IsEffectivelyVisible);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ElevationBanner_IsHiddenWhileElevated()
    {
        await fixture.Session.Dispatch(() =>
        {
            var window = CreateWindow(new EmptyScanner(), isElevated: true);
            window.Show();
            window.UpdateLayout();

            var banner = Assert.Single(
                window.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "未提权 · 部分系统缓存不可清理");
            Assert.False(banner.IsEffectivelyVisible);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RowDetail_RendersEveryCleanRootOnItsOwnLine()
    {
        await fixture.Session.Dispatch(async () =>
        {
            var viewModel = CreateViewModel(new MultiPathScanner());
            var window = CreateWindow(viewModel);
            window.Show();

            viewModel.PrimaryCommand.Execute(null);
            for (var attempt = 0; attempt < 200 && viewModel.State != WorkflowState.Results; attempt++)
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }

            var row = viewModel.Items.Single(item => item.Id == "multi");
            viewModel.Groups[0].IsExpanded = true;
            row.IsExpanded = true;
            window.UpdateLayout();

            Assert.Equal(2, row.PathLines.Count);
            var rendered = window.GetVisualDescendants()
                .OfType<TextBlock>()
                .Where(text => text.Text is not null && row.PathLines.Contains(text.Text))
                .ToArray();
            Assert.Equal(2, rendered.Length);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Groups_CollapseAndExpandTheirRows()
    {
        await fixture.Session.Dispatch(async () =>
        {
            var viewModel = CreateViewModel(new AlignmentScanner());
            var window = CreateWindow(viewModel);
            window.Show();

            viewModel.PrimaryCommand.Execute(null);
            for (var attempt = 0; attempt < 200 && viewModel.State != WorkflowState.Results; attempt++)
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }

            window.UpdateLayout();
            var group = viewModel.Groups[0];
            var visibleRows = () => VisibleRowCount(window);

            // 默认折叠：组头在，行不在。
            Assert.False(group.IsExpanded);
            Assert.Equal(0, visibleRows());
            Assert.Equal(-90, group.ChevronRotation);

            group.IsExpanded = true;
            window.UpdateLayout();

            var expanded = visibleRows();
            Assert.True(expanded > 0);
            Assert.Equal(0, group.ChevronRotation);

            group.IsExpanded = false;
            window.UpdateLayout();

            Assert.Equal(0, visibleRows());
            Assert.Equal(-90, group.ChevronRotation);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>手风琴：展开一个分组时，其它分组的行不渲染。</summary>
    [Fact]
    public async Task Groups_KeepOnlyOneExpandedAtATime()
    {
        await fixture.Session.Dispatch(async () =>
        {
            var viewModel = CreateViewModel(new TwoGroupScanner());
            var window = CreateWindow(viewModel);
            window.Show();

            viewModel.PrimaryCommand.Execute(null);
            for (var attempt = 0; attempt < 200 && viewModel.State != WorkflowState.Results; attempt++)
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }

            window.UpdateLayout();

            Assert.Equal(2, viewModel.Groups.Count);
            Assert.Equal(0, VisibleRowCount(window));

            viewModel.Groups[0].IsExpanded = true;
            window.UpdateLayout();
            var firstGroupRows = VisibleRowCount(window);
            Assert.Equal(1, firstGroupRows);

            viewModel.Groups[1].IsExpanded = true;
            window.UpdateLayout();

            Assert.False(viewModel.Groups[0].IsExpanded);
            Assert.True(viewModel.Groups[1].IsExpanded);
            Assert.Equal(firstGroupRows, VisibleRowCount(window));
            Assert.Equal(["cache"], viewModel.Groups[1].Items.Select(item => item.Id));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GhostRow_AppearsOnlyWhileScanning()
    {
        await fixture.Session.Dispatch(async () =>
        {
            var scanner = new BlockingScanner();
            var viewModel = CreateViewModel(scanner);
            var window = CreateWindow(viewModel);
            window.Show();

            viewModel.PrimaryCommand.Execute(null);
            for (var attempt = 0; attempt < 200 && !viewModel.IsGhostVisible; attempt++)
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }

            window.UpdateLayout();

            Assert.True(viewModel.IsGhostVisible);
            Assert.Contains("正在扫描", viewModel.GhostText);
            Assert.True(Assert.Single(
                window.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Classes.Contains("ghostText")).IsEffectivelyVisible);
            Assert.True(Assert.Single(
                window.GetVisualDescendants().OfType<Border>(),
                border => border.Classes.Contains("scanSweep")).IsEffectivelyVisible);

            scanner.Release();
            for (var attempt = 0; attempt < 200 && viewModel.State != WorkflowState.Results; attempt++)
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }

            window.UpdateLayout();

            Assert.False(viewModel.IsGhostVisible);
            Assert.False(Assert.Single(
                window.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Classes.Contains("ghostText")).IsEffectivelyVisible);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CloseGuardModal_RendersWhileTheCloseConfirmationIsPending()
    {
        await fixture.Session.Dispatch(() =>
        {
            var viewModel = CreateViewModel(new EmptyScanner());
            var window = CreateWindow(viewModel);
            window.Show();
            window.UpdateLayout();

            var modal = window.GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(text => text.Text == "确认关闭");
            Assert.False(modal.IsEffectivelyVisible);

            viewModel.RequestCloseConfirmation();
            window.UpdateLayout();

            Assert.True(modal.IsEffectivelyVisible);
            Assert.Contains(
                window.GetVisualDescendants().OfType<Button>(),
                button => Equals(button.Content, "停止并退出"));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>当前真正渲染出来的结果行数（折叠的分组不计）。</summary>
    private static int VisibleRowCount(Window window) => window.GetVisualDescendants()
        .OfType<ToggleButton>()
        .Count(row => row.Classes.Contains("rowMain") && row.IsEffectivelyVisible);

    private static MainWindow CreateWindow(ICleanupScanner scanner, bool isElevated = true) =>
        CreateWindow(CreateViewModel(scanner, isElevated: isElevated));

    private static MainWindow CreateWindow(MainWindowViewModel viewModel) =>
        new() { DataContext = viewModel };

    private static MainWindowViewModel CreateViewModel(
        ICleanupScanner scanner,
        ICleanupExecutor? executor = null,
        bool isElevated = true) => new(
        scanner,
        executor ?? new EmptyExecutor(),
        new CleanupSafetyPolicy(),
        new DiskSnapshot("C:", "NTFS", 255L * 1024 * 1024 * 1024, 73L * 1024 * 1024 * 1024),
        new InMemoryLogStore(NullApplicationLogger.Instance),
        new FakeElevationService(isElevated));

    private sealed class FakeElevationService(bool isElevated) : IElevationService
    {
        public bool IsElevated { get; } = isElevated;

        public bool RestartElevated() => false;
    }

    private sealed class EmptyScanner : ICleanupScanner
    {
        public Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default, bool skipSystemAnalysis = false) =>
            Task.FromResult(new ScanResult(new("C:", "NTFS", 1, 1), [], TimeSpan.Zero));
    }

    private sealed class EmptyExecutor : ICleanupExecutor
    {
        public Task<CleanupResult> CleanAsync(IReadOnlyList<CleanupItem> plan, IProgress<CleanupProgress>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CleanupResult([], TimeSpan.Zero));
    }

    /// <summary>三行同一分组，用来断言行宽与列宽一致。</summary>
    private sealed class AlignmentScanner : ICleanupScanner
    {
        public Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default, bool skipSystemAnalysis = false)
        {
            CleanupItem[] items =
            [
                new("short", "Short", @"C:\Temp", CleanupCategory.TemporaryFiles, CleanupRisk.Low, 1_024, 2, "", "short",
                    CleanerKind: CleanerKind.DirectoryContents, Paths: [@"C:\Temp"]),
                new("medium", "Medium length item", @"C:\Users\test\AppData\Local\NuGet", CleanupCategory.TemporaryFiles, CleanupRisk.Low, 2_048, 30, "", "medium",
                    CleanerKind: CleanerKind.DirectoryContents, Paths: [@"C:\Users\test\AppData\Local\NuGet"]),
                new("long", "A much longer cleanup result item name", @"C:\Users\test\.nuget\packages\a\b\c", CleanupCategory.TemporaryFiles, CleanupRisk.Medium, 4_096, 4_000, "", "long",
                    CleanerKind: CleanerKind.DirectoryContents, Paths: [@"C:\Users\test\.nuget\packages\a\b\c"])
            ];
            return Task.FromResult(new ScanResult(new("C:", "NTFS", 100_000, 40_000), items, TimeSpan.Zero));
        }
    }

    /// <summary>两个分组各一行，用来验证手风琴（同一时刻只展开一个）。</summary>
    private sealed class TwoGroupScanner : ICleanupScanner
    {
        public Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default, bool skipSystemAnalysis = false)
        {
            CleanupItem[] items =
            [
                new("temp", "过期临时文件", @"C:\Temp", CleanupCategory.TemporaryFiles, CleanupRisk.Low, 1_024, 2, "", "temp",
                    CleanerKind: CleanerKind.DirectoryContents, Paths: [@"C:\Temp"]),
                new("cache", "NuGet 全局包缓存", @"C:\Users\test\.nuget\packages", CleanupCategory.PackageCache, CleanupRisk.Medium, 2_048, 3, "", "cache",
                    CleanerKind: CleanerKind.DirectoryContents, Paths: [@"C:\Users\test\.nuget\packages"])
            ];
            return Task.FromResult(new ScanResult(new("C:", "NTFS", 100_000, 40_000), items, TimeSpan.Zero));
        }
    }

    private sealed class MultiPathScanner : ICleanupScanner
    {
        public Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default, bool skipSystemAnalysis = false)
        {
            CleanupItem[] items =
            [
                new("multi", "Edge / Chrome 缓存", @"C:\Users\test\AppData\Local\Microsoft\Edge\User Data 等 2 个目录",
                    CleanupCategory.BrowserCache, CleanupRisk.Low, 4_096, 12, "浏览器缓存。", "multi",
                    CleanerKind: CleanerKind.DirectoryContents,
                    Paths:
                    [
                        @"C:\Users\test\AppData\Local\Microsoft\Edge\User Data\Default\Cache",
                        @"C:\Users\test\AppData\Local\Google\Chrome\User Data\Default\Cache"
                    ],
                    PathSource: "目录探测")
            ];
            return Task.FromResult(new ScanResult(new("C:", "NTFS", 100_000, 40_000), items, TimeSpan.Zero));
        }
    }

    /// <summary>报告一行已完成、第二行正在分析后挂起，用来渲染幽灵行。</summary>
    private sealed class BlockingScanner : ICleanupScanner
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _gate.TrySetResult();

        public async Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default, bool skipSystemAnalysis = false)
        {
            CleanupItem[] items =
            [
                new("first", "Windows 临时目录", @"C:\Windows\Temp", CleanupCategory.TemporaryFiles, CleanupRisk.Low, 1_024, 2, "", "first",
                    CleanerKind: CleanerKind.DirectoryContents, Paths: [@"C:\Windows\Temp"]),
                new("second", "组件存储 WinSxS", @"C:\Windows\WinSxS", CleanupCategory.SystemFiles, CleanupRisk.High, 0, 0, "", null,
                    Paths: [@"C:\Windows\WinSxS"])
            ];
            progress?.Report(new(0, 3, items[0].DisplayName));
            progress?.Report(new(1, 3, items[0].DisplayName, ScanTier.Fast, items[0]));
            progress?.Report(new(1, 3, items[1].DisplayName, ScanTier.Slow));
            await _gate.Task.WaitAsync(cancellationToken);
            progress?.Report(new(2, 3, items[1].DisplayName, ScanTier.Slow, items[1]));
            return new ScanResult(new("C:", "NTFS", 100_000, 40_000), items, TimeSpan.Zero);
        }
    }
}
