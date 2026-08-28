using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.VisualTree;
using ClearC.Core.Models;
using ClearC.Core.Safety;
using ClearC.Core.Services;
using ClearC.Desktop.Controls;
using ClearC.Desktop.ViewModels;
using ClearC.Desktop.Views;

namespace ClearC.Desktop.Tests;

public sealed class MainWindowHeadlessTests(AvaloniaHeadlessFixture fixture) : IClassFixture<AvaloniaHeadlessFixture>
{
    [Fact]
    public async Task MainWindow_MatchesPrototypeDimensionsAndLoadsCoreControls()
    {
        await fixture.Session.Dispatch(() =>
        {
            var window = new MainWindow
            {
                DataContext = new MainWindowViewModel(
                    new EmptyScanner(),
                    new EmptyExecutor(),
                    new CleanupSafetyPolicy(),
                    new DiskSnapshot("C:", "NTFS", 255L * 1024 * 1024 * 1024, 73L * 1024 * 1024 * 1024))
            };
            window.Show();

            Assert.Equal(1060, window.Width);
            Assert.Equal(700, window.Height);
            Assert.Single(window.GetVisualDescendants().OfType<DiskDonut>());
            Assert.Single(window.GetVisualDescendants().OfType<DataGrid>());
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "⌕  扫描分析"));
            Assert.IsType<TitleBarViewModel>(Assert.Single(window.GetVisualDescendants().OfType<TitleBarView>()).DataContext);
            Assert.IsType<CleanupWorkspaceViewModel>(Assert.Single(window.GetVisualDescendants().OfType<CleanupWorkspaceView>()).DataContext);
            Assert.IsType<LogPanelViewModel>(Assert.Single(window.GetVisualDescendants().OfType<LogPanelView>()).DataContext);
            Assert.IsType<StatusBarViewModel>(Assert.Single(window.GetVisualDescendants().OfType<StatusBarView>()).DataContext);
            Assert.IsType<WorkflowOverlayViewModel>(Assert.Single(window.GetVisualDescendants().OfType<WorkflowOverlayView>()).DataContext);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CleanupRows_FillTheDataGridAndShareColumnPositions()
    {
        await fixture.Session.Dispatch(async () =>
        {
            var viewModel = new MainWindowViewModel(
                new AlignmentScanner(),
                new EmptyExecutor(),
                new CleanupSafetyPolicy(),
                new DiskSnapshot("C:", "NTFS", 255L * 1024 * 1024 * 1024, 73L * 1024 * 1024 * 1024));
            var window = new MainWindow { DataContext = viewModel };
            window.Show();

            viewModel.PrimaryCommand.Execute(null);
            for (var attempt = 0; attempt < 100 && viewModel.State != WorkflowState.Results; attempt++)
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }

            Assert.Equal(WorkflowState.Results, viewModel.State);
            window.UpdateLayout();

            var dataGrid = Assert.Single(window.GetVisualDescendants().OfType<DataGrid>());
            var cells = dataGrid.GetVisualDescendants().OfType<DataGridCell>().ToArray();
            Assert.NotEmpty(cells);
            Assert.All(cells, cell => Assert.Equal(HorizontalAlignment.Stretch, cell.HorizontalContentAlignment));

            var rows = dataGrid.GetVisualDescendants()
                .OfType<ToggleButton>()
                .Where(row => row.Classes.Contains("cleanupRow"))
                .ToArray();
            Assert.Equal(3, rows.Length);
            var rowOffsets = rows.Select(row => row.TranslatePoint(default, dataGrid)!.Value.X).ToArray();
            Assert.Single(rowOffsets.Distinct());
            Assert.Single(rows.Select(row => row.Bounds.Width).Distinct());
            Assert.All(rowOffsets, offset => Assert.InRange(offset, 0, 1));
            Assert.All(rows, row => Assert.InRange(dataGrid.Bounds.Width - row.Bounds.Width, 0, 20));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    private sealed class EmptyScanner : ICleanupScanner
    {
        public Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ScanResult(new("C:", "NTFS", 1, 1), [], TimeSpan.Zero));
    }

    private sealed class EmptyExecutor : ICleanupExecutor
    {
        public Task<CleanupResult> CleanAsync(IReadOnlyList<CleanupItem> plan, IProgress<CleanupProgress>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CleanupResult([], TimeSpan.Zero));
    }

    private sealed class AlignmentScanner : ICleanupScanner
    {
        public Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            CleanupItem[] items =
            [
                new("short", "Short", @"C:\Temp", CleanupCategory.TemporaryFiles, CleanupRisk.Low, 1_024, 2, "", "user-temp"),
                new("medium", "Medium length item", @"C:\Users\test\AppData\Local\NuGet", CleanupCategory.PackageCache, CleanupRisk.Low, 2_048, 30, "", "nuget-http"),
                new("long", "A much longer cleanup result item name", @"C:\Users\test\.nuget\packages\a\b\c", CleanupCategory.PackageCache, CleanupRisk.Medium, 4_096, 4_000, "", "nuget-global")
            ];
            return Task.FromResult(new ScanResult(new("C:", "NTFS", 100_000, 40_000), items, TimeSpan.Zero));
        }
    }
}
