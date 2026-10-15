using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClearC.Core.Models;
using ClearC.Core.Safety;
using ClearC.Core.Services;
using ClearC.Desktop.I18n;
using ClearC.Desktop.Infrastructure.Logging;
using ClearC.Desktop.Infrastructure.Scanning;
using ClearC.Desktop.Services;
using ClearC.Desktop.ViewModels;
using ClearC.Desktop.Views;
using CodeWF.Avalonia.Controls.Themes;

namespace ClearC.Desktop;

internal static partial class SelfCheck
{
    // 使用可控样例驱动完整界面状态机，不枚举或清理用户的任何真实目录。
    private static void CheckWorkflow(string? captureDirectory)
    {
        var scanner = new WorkflowScanner();
        var executor = new WorkflowExecutor();
        var preferencePath = Path.Combine(Path.GetTempPath(), "clearc-workflow-" + Guid.NewGuid().ToString("N"), "appearance.json");
        var appearance = new AppearanceViewModel(new StudioThemeController(Application.Current!), new AppearanceSettingsStore(preferencePath));
        appearance.Mode = StudioThemeMode.Light;
        appearance.Palette = StudioPalette.DotNet;
        using var model = new MainWindowViewModel(scanner, executor, new CleanupSafetyPolicy(),
            new DiskSnapshot("C:", "NTFS", 256L * 1024 * 1024 * 1024, 64L * 1024 * 1024 * 1024),
            new InMemoryLogStore(NullApplicationLogger.Instance), elevationService: new FixtureElevation(),
            initialDrives: [new("C:", "NTFS", 256L << 30, 64L << 30), new("D:", "NTFS", 128L << 30, 16L << 30)],
            appearance: appearance);
        var window = new MainWindow { DataContext = model };
        try
        {
            window.Show();
            model.PrimaryCommand.Execute(null);
            WaitFor(WorkflowState.Scanning);
            Save("scanning");
            scanner.Complete();
            WaitFor(WorkflowState.Results);
            var expandedGroup = model.Sections.SelectMany(section => section.Groups).MaxBy(group => group.Count)!;
            expandedGroup.IsExpanded = true;
            var renderDeadline = DateTime.UtcNow.AddSeconds(5);
            while (window.GetVisualDescendants().OfType<ToggleButton>()
                       .Count(row => row.Classes.Contains("rowMain") && row.IsEffectivelyVisible) != expandedGroup.Count)
            {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                if (DateTime.UtcNow >= renderDeadline)
                {
                    Save("results-timeout");
                    throw new InvalidOperationException("The complete sample scan must render all result rows.");
                }
                Thread.Sleep(10);
            }
            var workspace = window.GetVisualDescendants().OfType<CleanupWorkspaceView>().Single();
            var toolbar = workspace.GetVisualDescendants().OfType<CheckBox>().First();
            var list = workspace.GetVisualDescendants().OfType<CleanupListView>().Single();
            if (list.TranslatePoint(default, window)!.Value.Y < toolbar.TranslatePoint(new Point(0, toolbar.Bounds.Height), window)!.Value.Y)
                throw new InvalidOperationException("Cleanup results overlap the filter toolbar.");
            Save("results");
            model.Items.First().IsSelected = true;
            model.PrimaryCommand.Execute(null);
            WaitFor(WorkflowState.Confirming);
            Save("confirming");
            model.ConfirmCleanupCommand.Execute(null);
            WaitFor(WorkflowState.Cleaning);
            Save("cleaning");
            executor.Complete();
            WaitFor(WorkflowState.Done);
            Save("done");
            if (!model.Items.Any(item => item.HasResult))
                throw new InvalidOperationException("Completed cleanup did not update the rendered result model.");
        }
        finally
        {
            window.Close();
            var directory = Path.GetDirectoryName(preferencePath)!;
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }

        void WaitFor(WorkflowState state)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (model.State != state)
            {
                Dispatcher.UIThread.RunJobs();
                if (DateTime.UtcNow >= deadline) throw new TimeoutException("Workflow did not reach " + state);
                Thread.Sleep(5);
            }
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }

        void Save(string state)
        {
            if (captureDirectory is not null) Capture(window, captureDirectory, "workflow-" + AppLocalizer.Instance.Culture.Name + "-" + state + ".png");
        }
    }

    private sealed class WorkflowScanner : ICleanupScanner
    {
        private readonly TaskCompletionSource<ScanResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<CleanupItem> Items { get; } = new WindowsCleanupTargetCatalog().GetTargets()
            .Where(target => target.Id is "user-temp" or "thumbnail-icon-cache" or "wer-user")
            .Select((target, index) => target.ToItem((index + 1L) * 1024 * 1024, index + 1) with
            {
                Location = @"C:\CodeWF.SelfCheck\" + target.Id,
                Paths = [@"C:\CodeWF.SelfCheck\" + target.Id],
                RequiresElevation = false
            }).ToArray();

        public Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default,
            bool skipSystemAnalysis = false, IReadOnlyList<string>? driveScope = null)
        {
            progress?.Report(new ScanProgress(0, Items.Count, Items[0].DisplayName, TargetPath: Items[0].Location));
            return _completion.Task.WaitAsync(cancellationToken);
        }

        public void Complete() => _completion.SetResult(new ScanResult(new DiskSnapshot("C:", "NTFS", 256L * 1024 * 1024 * 1024,
            64L * 1024 * 1024 * 1024), Items, TimeSpan.FromSeconds(1)));
    }

    private sealed class WorkflowExecutor : ICleanupExecutor
    {
        private readonly TaskCompletionSource<CleanupResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private IReadOnlyList<CleanupItem> _plan = [];
        private IProgress<CleanupProgress>? _progress;

        public Task<CleanupResult> CleanAsync(IReadOnlyList<CleanupItem> plan, IProgress<CleanupProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            _plan = plan;
            _progress = progress;
            return _completion.Task.WaitAsync(cancellationToken);
        }

        public void Complete()
        {
            var results = _plan.Select(item => new CleanupItemResult(item.Id, CleanupOutcome.Completed, item.SizeBytes, string.Empty)).ToArray();
            for (var index = 0; index < _plan.Count; index++)
                _progress?.Report(new CleanupProgress(index + 1, _plan.Count, _plan[index], results[index]));
            _completion.SetResult(new CleanupResult(results, TimeSpan.FromSeconds(1)));
        }
    }
}
