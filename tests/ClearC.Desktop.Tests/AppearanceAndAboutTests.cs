using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClearC.Core.Models;
using ClearC.Core.Safety;
using ClearC.Desktop.I18n;
using ClearC.Desktop.Infrastructure.Scanning;
using ClearC.Desktop.Services;
using ClearC.Desktop.ViewModels;
using ClearC.Desktop.Views;
using CodeWF.Avalonia.Controls.Themes;
using CodeWF.Toolkit.Core.UpdateChecking;

namespace ClearC.Desktop.Tests;

[Collection(AvaloniaHeadlessFixture.CollectionName)]
public sealed class AppearanceAndAboutTests(AvaloniaHeadlessFixture fixture)
{
    [Fact]
    public async Task EightPalettesRenderLightAndDarkSurfacesWithRestorableSettings()
    {
        await fixture.Session.Dispatch(() =>
        {
            var application = Application.Current!;
            var previousTheme = application.RequestedThemeVariant;
            var previousCulture = AppLocalizer.Instance.Culture.Name;
            var temporary = Path.Combine(Path.GetTempPath(), "clearc-appearance-" + Guid.NewGuid().ToString("N"));
            var store = new AppearanceSettingsStore(Path.Combine(temporary, "appearance.json"));
            try
            {
                using var appearance = new AppearanceViewModel(new StudioThemeController(application), store);
                Assert.Equal(8, appearance.Palettes.Count);
                Assert.Equal(3, appearance.Modes.Count);
                var window = new MainWindow();
                window.Show();
                foreach (var palette in Enum.GetValues<StudioPalette>())
                foreach (var mode in new[] { StudioThemeMode.Light, StudioThemeMode.Dark })
                {
                    appearance.Palette = palette;
                    appearance.Mode = mode;
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    Assert.True(application.TryGetResource("StudioBackgroundColor", application.ActualThemeVariant, out var expected));
                    Assert.Equal(Assert.IsType<Color>(expected), Assert.IsType<SolidColorBrush>(window.Background).Color);
                    Assert.Equal(palette, store.Load().Palette);
                    Assert.Equal(mode, store.Load().Mode);
                }

                appearance.CultureName = "en-US";
                Assert.Equal("en-US", store.Load().CultureName);
                Assert.Equal("System", appearance.Modes[0].DisplayName);
                Assert.Equal("en-US", new AppearanceSettingsStore(Path.Combine(temporary, "appearance.json")).Load().CultureName);
                window.Close();
            }
            finally
            {
                AppLocalizer.Instance.SetCulture(previousCulture);
                application.RequestedThemeVariant = previousTheme;
                if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ExistingCatalogItemAndAboutWindowRefreshAfterLanguageSwitch()
    {
        await fixture.Session.Dispatch(() =>
        {
            var previous = AppLocalizer.Instance.Culture.Name;
            try
            {
                AppLocalizer.Instance.SetCulture("zh-CN");
                var target = new WindowsCleanupTargetCatalog().GetTargets().Single(item => item.Id == "user-temp");
                var item = new CleanupItemViewModel(target.ToItem(10, 1));
                using var updates = new UpdateViewModel((_, _, _) => { }, new StubUpdates());
                var about = new AboutWindow { DataContext = new AboutViewModel(updates) };
                about.Show();
                Assert.Equal("过期临时文件", item.DisplayName);
                AppLocalizer.Instance.SetCulture("en-US");
                about.UpdateLayout();
                Assert.Equal("Expired temporary files", item.DisplayName);
                Assert.Contains("seven days", item.Description);
                Assert.Contains(about.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>(), text => text.Text == "Build time");
                Assert.DoesNotContain(about.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>(), text => text.Text?.StartsWith("ClearC.", StringComparison.Ordinal) == true);
                Assert.StartsWith("▍ Scanning", new ScanProgress(0, 1, "Temp").GhostText);
                var decision = new CleanupSafetyPolicy().Evaluate(target.ToItem(10, 1) with { Paths = [@"C:\Users\test\Documents"] }, false);
                Assert.Contains("protected", decision.Reason);
                about.Close();
            }
            finally { AppLocalizer.Instance.SetCulture(previous); }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task VerifiedUpdateCanDownloadAndInstallThroughToolkitService()
    {
        await fixture.Session.Dispatch(async () =>
        {
            var service = new StubUpdates { Result = new(NewUpdate(), true, null) };
            var exits = 0;
            using var model = new UpdateViewModel((_, _, _) => { }, service, exit: () => exits++);
            await model.AdvanceAsync();
            Assert.Equal(UpdateState.Available, model.State);
            await model.AdvanceAsync();
            Assert.Equal(UpdateState.Downloaded, model.State);
            await model.AdvanceAsync();
            Assert.Equal(UpdateState.Installed, model.State);
            Assert.Equal(1, service.DownloadCount);
            Assert.Equal(1, service.InstallCount);
            Assert.Equal(1, exits);
        }, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task MissingAssetOrChecksumOpensReleasePageWithoutDownloading(bool asset, bool checksum)
    {
        await fixture.Session.Dispatch(async () =>
        {
            var update = NewUpdate() with { AssetUrl = asset ? "https://example.org/setup.exe" : null, ChecksumUrl = checksum ? "https://example.org/setup.exe.sha256" : null };
            var service = new StubUpdates { Result = new(update, true, null) };
            string? opened = null;
            using var model = new UpdateViewModel((_, _, _) => { }, service, openUrl: url => opened = url);
            await model.AdvanceAsync();
            await model.AdvanceAsync();
            Assert.Equal(update.PageUrl, opened);
            Assert.Equal(0, service.DownloadCount);
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task FailedAndCanceledOperationsCanRetryAndCurrentStatusChangesLanguage()
    {
        await fixture.Session.Dispatch(async () =>
        {
            var previous = AppLocalizer.Instance.Culture.Name;
            try
            {
                var service = new StubUpdates { Result = UpdateCheckResult.Failed("offline") };
                using var model = new UpdateViewModel((_, _, _) => { }, service);
                await model.AdvanceAsync();
                Assert.Equal(UpdateState.Failed, model.State);
                Assert.Contains("offline", model.StatusText);
                service.BlockCheck = true;
                var checking = model.AdvanceAsync();
                Assert.True(model.IsBusy);
                model.Cancel();
                await checking;
                Assert.Equal(UpdateState.Canceled, model.State);
                service.BlockCheck = false;
                service.Result = UpdateCheckResult.Latest();
                await model.AdvanceAsync();
                Assert.Equal(UpdateState.Current, model.State);
                AppLocalizer.Instance.SetCulture("en-US");
                Assert.Equal("You are up to date.", model.StatusText);
            }
            finally { AppLocalizer.Instance.SetCulture(previous); }
        }, TestContext.Current.CancellationToken);
    }

    private static UpdateInfo NewUpdate() => new(new Version(9, 0, 0), "v9.0.0", "Release", "Notes",
        "https://github.com/dotnet9/ClearC/releases/tag/v9.0.0", "https://example.org/setup.exe", "setup.exe", "https://example.org/setup.exe.sha256", 100);

    [Fact]
    public async Task DownloadCancellationAndInstallerFailureRemainRecoverable()
    {
        await fixture.Session.Dispatch(async () =>
        {
            var service = new StubUpdates { Result = new(NewUpdate(), true, null), BlockDownload = true };
            var exits = 0;
            using var model = new UpdateViewModel((_, _, _) => { }, service, exit: () => exits++);
            await model.AdvanceAsync();
            var download = model.AdvanceAsync();
            Assert.True(model.IsDownloading);
            model.Cancel();
            await download;
            Assert.Equal(UpdateState.Canceled, model.State);
            Assert.Equal(0, service.InstallCount);

            service.BlockDownload = false;
            await model.AdvanceAsync();
            await model.AdvanceAsync();
            service.InstallError = new IOException("installer unavailable");
            await model.AdvanceAsync();
            Assert.Equal(UpdateState.Failed, model.State);
            Assert.Contains("installer unavailable", model.StatusText);
            Assert.False(model.IsBusy);
            Assert.Equal(0, exits);
            await model.CheckAsync();
            Assert.Equal(UpdateState.Available, model.State);
        }, TestContext.Current.CancellationToken);
    }

    private sealed class StubUpdates : IUpdateService
    {
        public UpdateServiceOptions Options { get; } = new() { Repository = "ClearC", ProductName = "ClearC" };
        public Version CurrentVersion => new(0, 2, 6);
        public string CurrentVersionText => "0.2.6";
        public UpdateCheckResult Result { get; set; } = UpdateCheckResult.Latest();
        public bool BlockCheck { get; set; }
        public bool BlockDownload { get; set; }
        public Exception? InstallError { get; set; }
        public int DownloadCount { get; private set; }
        public int InstallCount { get; private set; }

        public async Task<UpdateCheckResult> CheckAsync(Version? current = null, CancellationToken cancellationToken = default)
        {
            if (BlockCheck) await Task.Delay(Timeout.Infinite, cancellationToken);
            return Result;
        }

        public async Task<UpdateDownloadResult> DownloadAsync(UpdateInfo update, IProgress<UpdateDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            DownloadCount++;
            if (BlockDownload) await Task.Delay(Timeout.Infinite, cancellationToken);
            progress?.Report(new(100, 100));
            return new UpdateDownloadResult("test-setup.exe", "test-setup.exe", 100);
        }

        public Task<UpdateInstallResult> InstallAsync(UpdateDownloadResult download, CancellationToken cancellationToken = default)
        {
            InstallCount++;
            if (InstallError is not null) throw InstallError;
            return Task.FromResult(new UpdateInstallResult(UpdateInstallKind.Launched, "Started") { ShouldExitApplication = true });
        }
    }
}
