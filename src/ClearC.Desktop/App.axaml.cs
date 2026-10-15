using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ClearC.Core.Safety;
using ClearC.Desktop.I18n;
using ClearC.Desktop.Infrastructure.Cleanup;
using ClearC.Desktop.Infrastructure.Logging;
using ClearC.Desktop.Infrastructure.Scanning;
using ClearC.Desktop.Services;
using ClearC.Desktop.ViewModels;
using ClearC.Desktop.Views;
using CodeWF.Avalonia.Controls.Themes;

namespace ClearC.Desktop;

public sealed partial class App : Application
{
    public override void Initialize()
    {
        var reactiveBuilder = ReactiveUI.Builder.RxAppBuilder.CreateReactiveUIBuilder();
        reactiveBuilder.WithCoreServices();
        reactiveBuilder.BuildApp();
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        AppLocalizer.Initialize(new AppearanceSettingsStore().Load().CultureName ?? GetCultureName());
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownRequested += OnShutdownRequested;
            desktop.MainWindow = CreateMainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static string GetCultureName() =>
        CultureInfo.CurrentUICulture.Name.Equals("en-US", StringComparison.OrdinalIgnoreCase) ? "en-US" : "zh-CN";

    internal static MainWindow CreateMainWindow(IApplicationLogger? logger = null,
        bool checkUpdates = true, AppearanceSettingsStore? appearanceStore = null)
    {
        var scanner = new WindowsCleanupScanner();
        var executor = new WindowsCleanupExecutor();
        var diskProvider = new WindowsDiskInfoProvider();
        var disk = diskProvider.GetSystemDrive();
        var drives = diskProvider.GetFixedDrives();
        var logStore = new InMemoryLogStore(logger ?? CodeWfApplicationLogger.Instance);
        var window = new MainWindow();
        var appearance = Application.Current is { } application
            ? new AppearanceViewModel(new StudioThemeController(application), appearanceStore ?? new AppearanceSettingsStore(),
                error => logStore.Warning(AppText.Format(Language.AppearanceSaveFailed, error.Message), error))
            : null;
        var model = new MainWindowViewModel(
                scanner,
                executor,
                new CleanupSafetyPolicy(),
                disk,
                logStore,
                initialDrives: drives, appearance: appearance);
        window.DataContext = model;
        var about = new AboutWindowService(window, model.Update);
        model.AboutRequested += (_, _) => about.Show();
        if (checkUpdates) window.Opened += (_, _) => _ = model.Update.CheckAsync();
        window.Closed += (_, _) => model.Dispose();
        return window;
    }

    /// <summary>
    /// 绕过标题栏关闭（例如系统关机/注销）时也要取消进行中的任务并停掉 Toast 定时器（§3.2）。
    /// 关机路径上不能阻塞 UI 线程等待，因此这里只做取消。
    /// </summary>
    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow.DataContext: MainWindowViewModel viewModel })
        {
            viewModel.PrepareForShutdown();
        }
    }
}
