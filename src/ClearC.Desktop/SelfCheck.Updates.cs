using Avalonia.Threading;
using ClearC.Desktop.ViewModels;
using CodeWF.Toolkit.Core.UpdateChecking;

namespace ClearC.Desktop;

internal static partial class SelfCheck
{
    /// <summary>原生自检也验证完整更新状态，不访问网络或运行真实安装器。</summary>
    private static void CheckUpdates()
    {
        var info = new UpdateInfo(new Version(9, 0), "v9.0", "Release", "Notes", "https://example.org/release",
            "https://example.org/setup.exe", "setup.exe", "https://example.org/setup.exe.sha256");
        var service = new FixtureUpdates { Result = new(info, true, null) };
        var exits = 0;
        string? opened = null;
        using var model = new UpdateViewModel((_, _, _) => { }, service, openUrl: url => opened = url, exit: () => exits++);
        Run(model.AdvanceAsync()); Require(UpdateState.Available);
        Run(model.AdvanceAsync()); Require(UpdateState.Downloaded);
        Run(model.AdvanceAsync()); Require(UpdateState.Installed);
        if (exits != 1) throw new InvalidOperationException("Verified installer must request application exit.");
        service.Result = UpdateCheckResult.Failed("offline");
        Run(model.CheckAsync()); Require(UpdateState.Failed);
        service.Result = UpdateCheckResult.Latest();
        Run(model.CheckAsync()); Require(UpdateState.Current);
        service.BlockCheck = true;
        var checking = model.CheckAsync();
        Require(UpdateState.Checking);
        model.Cancel(); Run(checking); Require(UpdateState.Canceled);
        service.BlockCheck = false;
        service.Result = new(info, true, null);
        Run(model.CheckAsync());
        service.BlockDownload = true;
        var downloading = model.AdvanceAsync();
        Require(UpdateState.Downloading);
        model.Cancel(); Run(downloading); Require(UpdateState.Canceled);
        service.BlockDownload = false;
        service.Result = new(info with { AssetUrl = null, ChecksumUrl = null }, true, null);
        Run(model.CheckAsync()); Run(model.AdvanceAsync());
        if (opened != info.PageUrl) throw new InvalidOperationException("Missing installer must open the release page.");

        void Require(UpdateState expected)
        {
            if (model.State != expected) throw new InvalidOperationException($"Expected update state {expected}, actual {model.State}.");
        }
        static void Run(Task task)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!task.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                if (DateTime.UtcNow >= deadline) throw new TimeoutException("Update verification timed out.");
                Thread.Sleep(5);
            }
            task.GetAwaiter().GetResult();
        }
    }

    private sealed class FixtureUpdates : IUpdateService
    {
        public UpdateServiceOptions Options { get; } = new() { Repository = "ClearC", ProductName = "ClearC" };
        public Version CurrentVersion => new(0, 2, 7);
        public string CurrentVersionText => "0.2.7";
        public UpdateCheckResult Result { get; set; } = UpdateCheckResult.Latest();
        public bool BlockCheck { get; set; }
        public bool BlockDownload { get; set; }
        public async Task<UpdateCheckResult> CheckAsync(Version? current = null, CancellationToken cancellationToken = default)
        {
            if (BlockCheck) await Task.Delay(Timeout.Infinite, cancellationToken);
            return Result;
        }
        public async Task<UpdateDownloadResult> DownloadAsync(UpdateInfo update, IProgress<UpdateDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (BlockDownload) await Task.Delay(Timeout.Infinite, cancellationToken);
            progress?.Report(new(100, 100));
            return new("fixture-setup.exe", "fixture-setup.exe", 100);
        }
        public Task<UpdateInstallResult> InstallAsync(UpdateDownloadResult download, CancellationToken cancellationToken = default) =>
            Task.FromResult(new UpdateInstallResult(UpdateInstallKind.Launched, "Started") { ShouldExitApplication = true });
    }
}
