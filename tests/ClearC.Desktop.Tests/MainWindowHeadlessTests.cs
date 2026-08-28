using Avalonia.Controls;
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
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "⌕  扫描分析"));

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
}
