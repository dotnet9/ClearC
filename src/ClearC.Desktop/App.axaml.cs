using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ClearC.Core.Safety;
using ClearC.Desktop.Infrastructure.Cleanup;
using ClearC.Desktop.Infrastructure.Logging;
using ClearC.Desktop.Infrastructure.Scanning;
using ClearC.Desktop.ViewModels;
using ClearC.Desktop.Views;

namespace ClearC.Desktop;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownRequested += OnShutdownRequested;
            desktop.MainWindow = CreateMainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    internal static MainWindow CreateMainWindow(IApplicationLogger? logger = null)
    {
        var scanner = new WindowsCleanupScanner();
        var executor = new WindowsCleanupExecutor();
        var disk = new WindowsDiskInfoProvider().GetSystemDrive();
        var logStore = new InMemoryLogStore(logger ?? CodeWfApplicationLogger.Instance);
        return new MainWindow
        {
            DataContext = new MainWindowViewModel(
                scanner,
                executor,
                new CleanupSafetyPolicy(),
                disk,
                logStore)
        };
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
