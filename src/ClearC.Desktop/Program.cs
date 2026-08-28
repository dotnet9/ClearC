using Avalonia;
using CodeWF.Log.Core;
using Microsoft.Extensions.Logging;

namespace ClearC.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
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
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
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
}
