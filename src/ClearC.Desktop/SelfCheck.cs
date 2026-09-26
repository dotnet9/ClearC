using Avalonia;
using ClearC.Desktop.Infrastructure.Logging;
using ClearC.Desktop.Infrastructure.Scanning;

namespace ClearC.Desktop;

/// <summary>
/// <c>--selfcheck</c>（§8.2）：初始化 Avalonia、解析主题资源、构建主窗口、跑一次真实目录大小计算后退出 0。
/// 用于 CI 的 AOT 冒烟（<c>scripts/smoke.ps1</c>）。
/// </summary>
/// <remarks>
/// Avalonia 初始化后主线程会装上同步上下文，而自检不跑消息循环，
/// 因此这里全部用阻塞等待，异步工作放到线程池上执行。
/// </remarks>
internal static class SelfCheck
{
    public static int Run()
    {
        var probeDirectory = Path.Combine(Path.GetTempPath(), $"clearc-selfcheck-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(probeDirectory);
            File.WriteAllBytes(Path.Combine(probeDirectory, "a.bin"), new byte[4096]);

            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace()
                .SetupWithoutStarting();

            // selfcheck 不初始化 CodeWF 日志主机，因此用空日志器；日志面板的数据源仍会填充。
            _ = App.CreateMainWindow(NullApplicationLogger.Instance);

            var application = Application.Current;
            if (application is null ||
                !application.TryGetResource("ClearCAccent", application.ActualThemeVariant, out var accent) ||
                accent is null)
            {
                Console.Error.WriteLine("selfcheck: 主题资源 ClearCAccent 未解析。");
                return 1;
            }

            if (!application.TryGetResource("ClearCIconFile", application.ActualThemeVariant, out var icon) || icon is null)
            {
                Console.Error.WriteLine("selfcheck: 图标资源 ClearCIconFile 未解析。");
                return 1;
            }

            var size = Task
                .Run(() => new DirectorySizeCalculator().CalculateAsync(new DirectorySizeRequest([probeDirectory])))
                .GetAwaiter()
                .GetResult();

            if (size.Bytes < 4096 || size.FileCount != 1)
            {
                Console.Error.WriteLine($"selfcheck: 目录大小计算异常（{size.Bytes} 字节 / {size.FileCount} 个文件）。");
                return 1;
            }

            Console.WriteLine("selfcheck: OK");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"selfcheck: 失败 {exception}");
            return 1;
        }
        finally
        {
            try
            {
                if (Directory.Exists(probeDirectory))
                {
                    Directory.Delete(probeDirectory, recursive: true);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 临时目录清理失败不影响自检结论。
            }
        }
    }
}
