using ClearC.Core.Models;
using ClearC.Core.Safety;
using ClearC.Core.Services;
using ClearC.Desktop.ViewModels;

namespace ClearC.Desktop.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public async Task Scan_SelectsOnlyLowRiskCleanableItems()
    {
        var viewModel = CreateViewModel();

        viewModel.PrimaryCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Results);

        Assert.Equal(WorkflowState.Results, viewModel.State);
        Assert.Equal(1, viewModel.SelectedCount);
        Assert.Equal(1024, viewModel.SelectedBytes);
        Assert.True(viewModel.Items.Single(item => item.Id == "low").IsSelected);
        Assert.False(viewModel.Items.Single(item => item.Id == "medium").IsSelected);
    }

    [Fact]
    public async Task Cleanup_TransitionsThroughConfirmationAndDone()
    {
        var viewModel = CreateViewModel();
        viewModel.PrimaryCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Results);

        viewModel.PrimaryCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Confirming);
        Assert.True(viewModel.IsConfirmationVisible);

        viewModel.ConfirmCleanupCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Done);

        Assert.True(viewModel.IsToastVisible);
        Assert.Contains("1.0 KB", viewModel.ToastText);
        Assert.Equal("✓ 已清理", viewModel.Items.Single(item => item.Id == "low").ResultText);
    }

    [Fact]
    public async Task MediumRiskSelection_IsCalledOutInConfirmation()
    {
        var viewModel = CreateViewModel();
        viewModel.PrimaryCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Results);
        viewModel.Items.Single(item => item.Id == "medium").IsSelected = true;

        viewModel.PrimaryCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.State == WorkflowState.Confirming);

        Assert.True(viewModel.HasRiskSelection);
        Assert.Contains("中高风险", viewModel.ConfirmationWarning);
    }

    private static MainWindowViewModel CreateViewModel() => new(
        new FakeScanner(),
        new FakeExecutor(),
        new CleanupSafetyPolicy(),
        new DiskSnapshot("C:", "NTFS", 100_000, 40_000));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Delay(10, cancellationToken);
        }

        Assert.True(condition(), "The view model did not reach the expected state.");
    }

    private sealed class FakeScanner : ICleanupScanner
    {
        public Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            var items = new CleanupItem[]
            {
                new("low", "Low", @"C:\Temp", CleanupCategory.TemporaryFiles, CleanupRisk.Low, 1024, 2, "", "user-temp"),
                new("medium", "Medium", @"C:\Cache", CleanupCategory.PackageCache, CleanupRisk.Medium, 2048, 3, "", "nuget-global"),
                new("protected", "Protected", @"C:\Users\test\.codex", CleanupCategory.ApplicationData, CleanupRisk.High, 4096, 4, "", null, IsProtected: true)
            };
            progress?.Report(new(3, 3, "扫描完成"));
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
                var result = new CleanupItemResult(item.Id, CleanupOutcome.Completed, item.SizeBytes, "completed");
                results.Add(result);
                progress?.Report(new(index + 1, plan.Count, item, result));
            }

            return Task.FromResult(new CleanupResult(results, TimeSpan.FromSeconds(1)));
        }
    }
}
