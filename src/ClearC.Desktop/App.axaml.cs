using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ClearC.Core.Safety;
using ClearC.Desktop.Infrastructure.Cleanup;
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
            var scanner = new WindowsCleanupScanner();
            var executor = new WindowsCleanupExecutor();
            var disk = new WindowsDiskInfoProvider().GetSystemDrive();
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(scanner, executor, new CleanupSafetyPolicy(), disk)
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
