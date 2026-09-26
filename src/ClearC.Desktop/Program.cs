using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ClearC.Desktop.Infrastructure.Scanning;
using ClearC.Desktop.Infrastructure.Windows;
using ClearC.Desktop.ViewModels;
using CodeWF.Log.Core;
using Microsoft.Extensions.Logging;

namespace ClearC.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("ClearC 目前仅支持 Windows。");
            return 2;
        }

        if (args.Contains("--selfcheck", StringComparer.OrdinalIgnoreCase))
        {
            return SelfCheck.Run();
        }

        // 单实例（§2.7）：第二个实例把已有实例带到前台后自身退出。
        using var instance = SingleInstanceGuard.Acquire();
        if (!instance.IsFirstInstance)
        {
            SingleInstanceGuard.SignalExistingInstance();
            return 0;
        }

        var loggerInitialized = false;
        try
        {
            Logger.Initialize(new LoggerOptions
            {
                MinimumLevel = LogLevel.Information,
                EnableConsole = false,
                LineTemplate = "{Timestamp:HH:mm:ss} [{Level:u4}] {Message}{NewLine}",
                File = new FileLogOptions
                {
                    DirectoryPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "ClearC",
                        "Logs"),
                    MaxFileSizeBytes = 10L * 1024 * 1024,
                    RetentionDays = 14,
                    RetainedFileCountLimit = 10,
                    MaxDirectorySizeBytes = 100L * 1024 * 1024
                }
            });
            loggerInitialized = true;

            var builder = BuildAvaloniaApp();
            var lifetime = new ClassicDesktopStyleApplicationLifetime { Args = args, ShutdownMode = ShutdownMode.OnMainWindowClose };
            builder.SetupWithLifetime(lifetime);
            instance.StartListening(() => Dispatcher.UIThread.Post(() => BringToFront(lifetime)));
            return lifetime.Start(args);
        }
        catch (Exception exception)
        {
            if (loggerInitialized)
            {
                Logger.Fatal("ClearC 启动或运行失败。", exception);
            }

            throw;
        }
        finally
        {
            if (loggerInitialized)
            {
                Logger.ShutdownAsync().GetAwaiter().GetResult();
            }
        }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .UsePlatformDetect()
        .LogToTrace();

    private static void BringToFront(ClassicDesktopStyleApplicationLifetime lifetime)
    {
        if (lifetime.MainWindow is not { } window)
        {
            return;
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }
}
