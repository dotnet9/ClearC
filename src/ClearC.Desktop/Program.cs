using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using ClearC.Desktop.I18n;
using ClearC.Desktop.Infrastructure.Scanning;
using ClearC.Desktop.Infrastructure.Windows;
using ClearC.Desktop.ViewModels;
using CodeWF.Toolkit.Logging;
using Microsoft.Extensions.Logging;

namespace ClearC.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args is ["--verify-runtime", var directory])
        {
            RuntimeVerification.Run(directory);
            return 0;
        }
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine(AppText.Get("ClearC.System.PlatformWindowsOnly"));
            return 2;
        }

        if (args.Contains("--selfcheck", StringComparer.OrdinalIgnoreCase))
        {
            var captureIndex = Array.IndexOf(args, "--capture");
            return SelfCheck.Run(captureIndex >= 0 && captureIndex + 1 < args.Length ? args[captureIndex + 1] : null);
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
                Logger.Fatal(AppText.Get("ClearC.System.StartupFailed"), exception);
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
        // 保持中文界面使用无衬线字体，避免默认西文字体的系统回退变成宋体。
        .With(new FontManagerOptions
        {
            FontFallbacks =
            [
                new FontFallback { FontFamily = new FontFamily("Microsoft YaHei UI") },
                new FontFallback { FontFamily = new FontFamily("PingFang SC") },
                new FontFallback { FontFamily = new FontFamily("Noto Sans CJK SC") }
            ]
        })
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
