using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClearC.Core.Models;
using ClearC.Core.Safety;
using ClearC.Desktop.I18n;
using ClearC.Desktop.Infrastructure.Logging;
using ClearC.Desktop.Infrastructure.Scanning;
using ClearC.Desktop.Infrastructure.Windows;
using ClearC.Desktop.Services;
using ClearC.Desktop.ViewModels;
using ClearC.Desktop.Views;
using CodeWF.Avalonia.Controls.Themes;
using Ursa.Controls;

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
    public static int Run(string? captureDirectory = null, AppBuilder? builder = null)
    {
        var probeDirectory = Path.Combine(Path.GetTempPath(), $"clearc-selfcheck-{Guid.NewGuid():N}");
        var dataDirectory = Path.Combine(probeDirectory, "data");

        try
        {
            Directory.CreateDirectory(dataDirectory);
            File.WriteAllBytes(Path.Combine(dataDirectory, "a.bin"), new byte[4096]);

            (builder ?? Program.BuildAvaloniaApp()).SetupWithoutStarting();

            // selfcheck 不初始化 CodeWF 日志主机，因此用空日志器；日志面板的数据源仍会填充。
            var appearancePath = Path.Combine(probeDirectory, "appearance.json");
            var appearance = new AppearanceViewModel(new StudioThemeController(Application.Current!), new AppearanceSettingsStore(appearancePath));
            var window = CreateFixtureWindow(appearance);

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
            var themeControl = window.GetVisualDescendants().OfType<ThemeToggleButton>().Single();
            var themeButton = themeControl.GetVisualDescendants().OfType<Button>().Single(button => button.Name == ThemeToggleButton.PART_ThemeButton);
            model.Appearance!.Palette = StudioPalette.Aurora;
            model.Appearance.Mode = StudioThemeMode.System;
            foreach (var expectedMode in new[] { StudioThemeMode.Light, StudioThemeMode.Dark, StudioThemeMode.System })
            {
                themeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                if (model.Appearance.Mode != expectedMode || model.Appearance.Palette != StudioPalette.Aurora)
                    throw new InvalidOperationException("Title theme button must cycle three modes without replacing the palette.");
            }
            if (window.FindControl<Button>("TitleLanguageButton")?.Flyout is not MenuFlyout { Items.Count: 2 } languageMenu)
                throw new InvalidOperationException("Title language icon must open the actual language menu.");
            foreach (var item in languageMenu.Items.OfType<MenuItem>())
            {
                languageMenu.ShowAt(window.FindControl<Button>("TitleLanguageButton")!);
                Dispatcher.UIThread.RunJobs();
                item.Command!.Execute(item.CommandParameter);
                Dispatcher.UIThread.RunJobs();
                languageMenu.Hide();
                if (model.Appearance.CultureName != item.CommandParameter?.ToString())
                    throw new InvalidOperationException("Language menu must update the single global culture.");
            }
            var previousCulture = AppLocalizer.Instance.Culture.Name;
            var localeEditor = new TextBox { Text = window.Title };
            var localeWindow = new Window { Content = localeEditor, Width = 320, Height = 100 };
            localeWindow.Show(window);
            Dispatcher.UIThread.RunJobs();
            var editMenu = localeEditor.ContextFlyout as MenuFlyout ?? throw new InvalidOperationException("Semi editor menu is missing.");
            foreach (var culture in new[] { "zh-CN", "en-US", "zh-CN", "en-US" })
            {
                CodeWF.Toolkit.Lang.I18nManager.Instance.Culture = CultureInfo.GetCultureInfo(culture);
                Dispatcher.UIThread.RunJobs();
                ResourceDictionary semiText = culture == "en-US" ? new Semi.Avalonia.Locale.en_us() : new Semi.Avalonia.Locale.zh_cn();
                ResourceDictionary ursaText = culture == "en-US" ? new Ursa.Themes.Semi.Locale.en_us() : new Ursa.Themes.Semi.Locale.zh_cn();
                editMenu.ShowAt(localeEditor);
                Dispatcher.UIThread.RunJobs();
                if (((MenuItem)editMenu.Items[1]!).Header?.ToString() != semiText["STRING_MENU_COPY"]?.ToString() ||
                    ((MenuItem)editMenu.Items[2]!).Header?.ToString() != semiText["STRING_MENU_PASTE"]?.ToString())
                    throw new InvalidOperationException("Existing Semi copy and paste menus must follow the global culture.");
                editMenu.Hide();
                var ursaTheme = application.Styles.OfType<Ursa.Themes.Semi.UrsaSemiTheme>().Single();
                if (ursaTheme.Resources["STRING_MENU_DIALOG_CANCEL"]?.ToString() != ursaText["STRING_MENU_DIALOG_CANCEL"]?.ToString())
                    throw new InvalidOperationException("Existing Ursa resources must follow the global culture.");
            }
            var hostCopyText = window.Title;
            application.Resources["STRING_MENU_COPY"] = hostCopyText;
            AppLocalizer.Instance.SetCulture("zh-CN");
            Dispatcher.UIThread.RunJobs();
            editMenu.ShowAt(localeEditor);
            Dispatcher.UIThread.RunJobs();
            if (((MenuItem)editMenu.Items[1]!).Header?.ToString() != hostCopyText)
                throw new InvalidOperationException("Host resource overrides must survive third-party language updates.");
            editMenu.Hide();
            application.Resources.Remove("STRING_MENU_COPY");
            AppLocalizer.Instance.SetCulture(previousCulture);
            localeWindow.Close();
            Console.WriteLine("Global culture, existing Semi/Ursa controls and host resource override verification passed.");
            foreach (var culture in new[] { "zh-CN", "en-US" })
                foreach (var palette in Enum.GetValues<StudioPalette>())
                    foreach (var mode in new[] { StudioThemeMode.Light, StudioThemeMode.Dark })
                    {
                        AppLocalizer.Instance.SetCulture(culture);
                        model.Appearance!.Palette = palette;
                        model.Appearance.Mode = mode;
                        Dispatcher.UIThread.RunJobs();
                        window.UpdateLayout();
                        if (!application.TryGetResource("StudioBackgroundColor", application.ActualThemeVariant, out var expected) ||
                            window.Background is not SolidColorBrush brush || brush.Color != (Color)expected!)
                        {
                            throw new InvalidOperationException(AppText.Format(Language.SystemSelfCheckThemeMismatch, palette, mode));
                        }
                        var swatches = window.GetVisualDescendants().OfType<ListBox>().Single(list => list.Classes.Contains("paletteSwatches"));
                        if (swatches.Items.Count != 8 || swatches.SelectedItem is null ||
                            window.GetVisualDescendants().OfType<ComboBox>().Any(combo => combo.IsEffectivelyVisible))
                            throw new InvalidOperationException("Title appearance tools must retain eight swatches and avoid duplicate selectors.");
                        if (culture == "en-US" && window.GetVisualDescendants().OfType<TextBlock>()
                            .Where(text => text.IsEffectivelyVisible && !text.GetVisualAncestors().OfType<LogPanelView>().Any())
                            .Any(text => System.Text.RegularExpressions.Regex.IsMatch(text.Text ?? string.Empty, @"[\u4e00-\u9fff]")))
                            throw new InvalidOperationException("English interface contains untranslated text.");
                        var saved = new AppearanceSettingsStore(appearancePath).Load();
                        if (saved.Mode != mode || saved.Palette != palette || saved.CultureName != culture)
                            throw new InvalidOperationException("Appearance preferences did not persist the selected values.");
                        if (captureDirectory is not null) Capture(window, captureDirectory, $"main-{culture}-{palette}-{mode}.png");
                    }
            model.Appearance!.Mode = StudioThemeMode.System;
            if (new AppearanceSettingsStore(appearancePath).Load().Mode != StudioThemeMode.System)
                throw new InvalidOperationException("The system appearance mode must persist independently from the palette.");
            model.Appearance.Mode = StudioThemeMode.Light;
            model.Appearance.Palette = StudioPalette.DotNet;

            var about = new AboutWindow { DataContext = new AboutViewModel(model.Update) };
            about.Show(window);
            Dispatcher.UIThread.RunJobs();
            about.UpdateLayout();
            var aboutMinimize = about.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "PART_MinimizeButton");
            var aboutMaximize = about.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "PART_MaximizeRestoreButton");
            if (!aboutMinimize.IsEffectivelyVisible || aboutMinimize.IsEnabled || !aboutMaximize.IsEffectivelyVisible || aboutMaximize.IsEnabled)
                throw new InvalidOperationException("Actual About caption must disable minimizing and maximizing.");
            aboutMinimize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            aboutMaximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (about.WindowState != WindowState.Normal ||
                !about.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "PART_CloseButton").IsEnabled)
                throw new InvalidOperationException("Disabled About actions must preserve the normal window and leave closing available.");
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
                // 系统清理目录仅存在于 Windows，不在其他平台伪造可清理目标。
                if (OperatingSystem.IsWindows())
                {
                    var target = new WindowsCleanupTargetCatalog().GetTargets().Single(target => target.Id == "user-temp");
                    var item = new CleanupItemViewModel(target.ToItem(10, 1));
                    if (item.DisplayName != AppText.Get(Language.TargetsUserTempName))
                        throw new InvalidOperationException(AppText.Format(Language.SystemSelfCheckLanguageMismatch, culture));
                }
                if (captureDirectory is not null) Capture(about, captureDirectory, $"about-{culture}.png");
            }
            AppLocalizer.Instance.SetCulture(previousCulture);
            about.Close();
            window.Close();

            foreach (var culture in new[] { "zh-CN", "en-US" })
            {
                AppLocalizer.Instance.SetCulture(culture);
                if (OperatingSystem.IsWindows()) CheckWorkflow(captureDirectory);
                CheckUpdates();
            }
            AppLocalizer.Instance.SetCulture(previousCulture);

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
        var measurements = new List<string> { "key,x,y,width,height" };
        foreach (var (key, visual) in new (string, Visual?)[]
        {
            ("window", window),
            ("workspace", window.GetVisualDescendants().OfType<CleanupWorkspaceView>().SingleOrDefault()),
            ("donut", window.GetVisualDescendants().OfType<Controls.DiskDonut>().SingleOrDefault()),
            ("list", window.GetVisualDescendants().OfType<CleanupListView>().SingleOrDefault()),
            ("console", window.GetVisualDescendants().OfType<LogPanelView>().SingleOrDefault()),
            ("statusbar", window.GetVisualDescendants().OfType<StatusBarView>().SingleOrDefault())
        })
        {
            if (visual is null) continue;
            var position = visual.TranslatePoint(default, window) ?? default;
            measurements.Add(string.Join(",", key, position.X.ToString(CultureInfo.InvariantCulture), position.Y.ToString(CultureInfo.InvariantCulture),
                visual.Bounds.Width.ToString(CultureInfo.InvariantCulture), visual.Bounds.Height.ToString(CultureInfo.InvariantCulture)));
        }
        File.WriteAllLines(Path.Combine(directory, Path.GetFileNameWithoutExtension(name) + ".geometry.csv"), measurements);
    }

    private static MainWindow CreateFixtureWindow(AppearanceViewModel? appearance)
    {
        var system = new DiskSnapshot("C:", "NTFS", 256L << 30, 64L << 30);
        var model = new MainWindowViewModel(new WorkflowScanner(), new WorkflowExecutor(), new CleanupSafetyPolicy(), system,
            new InMemoryLogStore(NullApplicationLogger.Instance), elevationService: new FixtureElevation(),
            initialDrives: [system, new DiskSnapshot("D:", "NTFS", 128L << 30, 16L << 30)], appearance: appearance);
        var window = new MainWindow { DataContext = model };
        window.Closed += (_, _) => model.Dispose();
        return window;
    }

    private sealed class FixtureElevation : IElevationService
    {
        public bool IsElevated => false;
        public bool RestartElevated() => false;
    }
}
