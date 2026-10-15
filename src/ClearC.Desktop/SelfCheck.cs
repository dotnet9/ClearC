using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClearC.Desktop.Infrastructure.Logging;
using ClearC.Desktop.Infrastructure.Scanning;
using ClearC.Desktop.I18n;
using ClearC.Desktop.Services;
using ClearC.Desktop.ViewModels;
using ClearC.Desktop.Views;
using CodeWF.Avalonia.Controls.Themes;

namespace ClearC.Desktop;

/// <summary>
/// <c>--selfcheck</c>（§8.2）：初始化 Avalonia、解析主题资源、构建主窗口、跑一次真实目录大小计算后退出 0。
/// 用于 CI 的 AOT 冒烟（<c>scripts/smoke.ps1</c>）。
/// </summary>
/// <remarks>
/// Avalonia 初始化后主线程会装上同步上下文，而自检不跑消息循环，
/// 因此这里全部用阻塞等待，异步工作放到线程池上执行。
/// </remarks>
internal static partial class SelfCheck
{
    public static int Run(string? captureDirectory = null)
    {
        var probeDirectory = Path.Combine(Path.GetTempPath(), $"clearc-selfcheck-{Guid.NewGuid():N}");
        var dataDirectory = Path.Combine(probeDirectory, "data");

        try
        {
            Directory.CreateDirectory(dataDirectory);
            File.WriteAllBytes(Path.Combine(dataDirectory, "a.bin"), new byte[4096]);

            Program.BuildAvaloniaApp().SetupWithoutStarting();

            // selfcheck 不初始化 CodeWF 日志主机，因此用空日志器；日志面板的数据源仍会填充。
            var window = App.CreateMainWindow(NullApplicationLogger.Instance, checkUpdates: false,
                appearanceStore: new AppearanceSettingsStore(Path.Combine(probeDirectory, "appearance.json")));

            var application = Application.Current;
            if (application is null ||
                !application.TryGetResource("ClearCAccent", application.ActualThemeVariant, out var accent) ||
                accent is null)
            {
                Console.Error.WriteLine(AppText.Get("ClearC.System.SelfCheckAccentMissing"));
                return 1;
            }

            if (!application.TryGetResource("ClearCIconFile", application.ActualThemeVariant, out var icon) || icon is null)
            {
                Console.Error.WriteLine(AppText.Get("ClearC.System.SelfCheckIconMissing"));
                return 1;
            }

            window.Show();
            window.UpdateLayout();
            if (window.TitleBarHeight != 44 ||
                window.GetVisualDescendants().OfType<Controls.DiskDonut>().Single().Bounds.Width != 132 ||
                window.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("caption-button") && button.IsEffectivelyVisible)
                    .Any(button => button.Bounds.Height != 44 || button.Bounds.Width != 44))
                throw new InvalidOperationException("Window dimensions differ from the prototype.");
            var model = (MainWindowViewModel)window.DataContext!;
            var previousCulture = AppLocalizer.Instance.Culture.Name;
            foreach (var palette in Enum.GetValues<StudioPalette>())
            foreach (var mode in new[] { StudioThemeMode.Light, StudioThemeMode.Dark })
            {
                model.Appearance!.Palette = palette;
                model.Appearance.Mode = mode;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                if (!application.TryGetResource("StudioBackgroundColor", application.ActualThemeVariant, out var expected) ||
                    window.Background is not SolidColorBrush brush || brush.Color != (Color)expected!)
                {
                    throw new InvalidOperationException(AppText.Format(Language.SystemSelfCheckThemeMismatch, palette, mode));
                }
                if (captureDirectory is not null) Capture(window, captureDirectory, $"main-{palette}-{mode}.png");
            }

            var about = new AboutWindow { DataContext = new AboutViewModel(model.Update) };
            about.Show(window);
            foreach (var culture in new[] { "zh-CN", "en-US" })
            {
                AppLocalizer.Instance.SetCulture(culture);
                Dispatcher.UIThread.RunJobs();
                about.UpdateLayout();
                var displayed = about.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToArray();
                if (!displayed.Contains(AppText.Get(Language.AboutCompileTime)) || displayed.Any(text => text?.StartsWith("ClearC.", StringComparison.Ordinal) == true))
                {
                    throw new InvalidOperationException(AppText.Format(Language.SystemSelfCheckLanguageMismatch, culture));
                }
                var target = new WindowsCleanupTargetCatalog().GetTargets().Single(target => target.Id == "user-temp");
                var item = new CleanupItemViewModel(target.ToItem(10, 1));
                if (item.DisplayName != AppText.Get(Language.TargetsUserTempName))
                    throw new InvalidOperationException(AppText.Format(Language.SystemSelfCheckLanguageMismatch, culture));
                if (captureDirectory is not null) Capture(about, captureDirectory, $"about-{culture}.png");
            }
            AppLocalizer.Instance.SetCulture(previousCulture);
            about.Close();
            window.Close();

            CheckWorkflow(captureDirectory);

            var size = Task
                .Run(() => new DirectorySizeCalculator().CalculateAsync(new DirectorySizeRequest([dataDirectory])))
                .GetAwaiter()
                .GetResult();

            if (size.Bytes < 4096 || size.FileCount != 1)
            {
                Console.Error.WriteLine(AppText.Format("ClearC.System.SelfCheckDirectorySize", size.Bytes, size.FileCount));
                return 1;
            }

            Console.WriteLine("selfcheck: OK");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(AppText.Format("ClearC.System.SelfCheckFailed", exception));
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

    /// <summary>直接保存窗口渲染结果，不依赖桌面截图和鼠标操作。</summary>
    private static void Capture(Window window, string directory, string name)
    {
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height), new Vector(96, 96));
        bitmap.Render(window);
        bitmap.Save(Path.Combine(directory, name), new PngBitmapEncoderOptions());
    }
}
