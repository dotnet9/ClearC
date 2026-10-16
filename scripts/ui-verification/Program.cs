using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClearC.Desktop;
using ClearC.Desktop.Controls;
using ClearC.Desktop.Views;
using CodeWF.Avalonia.Controls.Themes;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

// 使用真实 Skia 渲染，不创建系统窗口，也不占用鼠标键盘。
var output = args.Length > 0 ? args[0] : "artifacts/ui-managed";
var builder = AppBuilder.Configure<App>()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .UseSkia();
var result = SelfCheck.Run(output, builder);
if (result != 0) return result;
var colors = new List<CheckboxColorEvidence>();
var assertions = new List<string>();
var check = new CheckBox { Content = "Selection", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
var host = new Window { Content = check, Width = 320, Height = 100 };
host.Show(); Pump();
var box = check.GetVisualDescendants().OfType<Border>().Single(item => item.Name == "PART_Box");
var glyph = check.GetVisualDescendants().OfType<IconGlyph>().Single();
foreach (var palette in Enum.GetValues<StudioPalette>())
foreach (var dark in new[] { false, true })
{
    Application.Current!.RequestedThemeVariant = StudioTheme.GetVariant(palette, dark); Pump();
    check.IsChecked = false; host.MouseMove(new Point(-20, -20)); Pump();
    Record("normal", false, true);
    var point = check.TranslatePoint(new Point(check.Bounds.Width / 2, check.Bounds.Height / 2), host)!.Value;
    host.MouseMove(point); Pump(); Record("hover", false, check.IsPointerOver);
    host.MouseMove(new Point(-20, -20)); check.IsChecked = true; Pump(); Record("checked", true, true);
    host.MouseMove(point); Pump(); Record("checked-hover", true, check.IsPointerOver);
    host.MouseMove(new Point(-20, -20)); check.IsEnabled = false; Pump();
    Record("disabled", true, !check.IsEffectivelyEnabled && box.Opacity < 1);
    check.IsEnabled = true; Pump(); Record("reenabled", true, check.IsEffectivelyEnabled);
    if (palette == StudioPalette.Aquatic && dark)
    {
        using var frame = host.CaptureRenderedFrame()!;
        using var stream = File.Create(Path.Combine(output, "checkbox-Aquatic-Dark.png"));
        frame.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    void Record(string state, bool selected, bool condition)
    {
        var foreground = (ISolidColorBrush)glyph.Foreground!;
        var background = (ISolidColorBrush)box.Background!;
        var ratio = Contrast(foreground.Color, background.Color);
        var variant = palette + " " + (dark ? "Dark" : "Light");
        colors.Add(new CheckboxColorEvidence(variant, state, foreground.Color.ToString(), background.Color.ToString(), glyph.Opacity, box.Opacity, ratio));
        var key = selected ? "StudioPrimary" : "ClearCWin";
        check.TryFindResource(key, check.ActualThemeVariant, out var resource);
        // 禁用态独立验证弱化反馈，启用选中态验证实际白勾与底色至少 3:1。
        if (!condition || (selected ? glyph.Opacity != 1 : glyph.Opacity != 0) ||
            resource is not ISolidColorBrush expected || expected.Color != background.Color ||
            selected && state != "disabled" && ratio < 3)
            throw new InvalidOperationException("Checkbox state failed: " + variant + " " + state + $"; ratio={ratio:F2}");
        assertions.Add(variant + " " + state); Console.WriteLine("PASS Checkbox " + assertions[^1] + $"; ratio={ratio:F2}");
    }
}
host.Close();
File.WriteAllText(Path.Combine(output, "checkbox-colors.json"), JsonSerializer.Serialize(colors, CheckboxJsonContext.Default.ListCheckboxColorEvidence));
File.WriteAllLines(Path.Combine(output, "checkbox-assertions.txt"), assertions);
Console.WriteLine($"Checkbox palette verification passed: {assertions.Count} assertions; NativeAOT={!RuntimeFeature.IsDynamicCodeSupported}; {output}");
var buttonColors = new List<ButtonColorEvidence>();
var buttonAssertions = new List<string>();
// 从四处真实视图提取按钮，保留模板和内容，只解除命令绑定，避免触发业务操作。
foreach (var (consumer, role, view) in new (string, string, Control)[]
{
    ("workspace-primary", "primary", new CleanupWorkspaceView()),
    ("about-primary", "primary", new AboutWindow()),
    ("workflow-confirm-primary", "primary", new WorkflowOverlayView()),
    ("workflow-close-danger", "danger", new WorkflowOverlayView())
})
{
    var button = view.GetLogicalDescendants().OfType<Button>().Single(item => item.Classes.Contains(role));
    ((Panel)button.Parent!).Children.Remove(button);
    button.ClearValue(Button.CommandProperty);
    button.ClearValue(Button.IsEnabledProperty);
    button.Command = new FixtureCommand();
    var contentText = button.GetLogicalDescendants().OfType<TextBlock>().FirstOrDefault();
    if (contentText is not null) contentText.Text = "Primary action";
    else button.Content = role == "primary" ? "Primary action" : "Stop and exit";
    foreach (var icon in button.GetLogicalDescendants().OfType<IconGlyph>()) icon.IconKey = "i-scan";
    var buttonHost = new Window { Content = button, Width = 320, Height = 100 };
    buttonHost.Show(); Pump();
    var root = button.GetVisualDescendants().OfType<Border>().Single(item => item.Name == "PART_Root");
    foreach (var palette in Enum.GetValues<StudioPalette>())
    foreach (var dark in new[] { false, true })
    {
        Application.Current!.RequestedThemeVariant = StudioTheme.GetVariant(palette, dark); Pump();
        buttonHost.TryFindResource("ClearCWin", buttonHost.ActualThemeVariant, out var surface);
        buttonHost.Background = (IBrush?)surface;
        button.IsEnabled = true; buttonHost.MouseMove(new Point(-20, -20)); Pump(); RecordButton("normal", !button.IsPointerOver && !button.IsPressed);
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), buttonHost)!.Value;
        buttonHost.MouseMove(point); Pump(); RecordButton("hover", button.IsPointerOver && !button.IsPressed);
        buttonHost.MouseDown(point, MouseButton.Left); Pump(); RecordButton("pressed", button.IsPressed);
        buttonHost.MouseMove(new Point(-20, -20)); buttonHost.MouseUp(new Point(-20, -20), MouseButton.Left);
        button.IsEnabled = false; Pump(); RecordButton("disabled", !button.IsEffectivelyEnabled && root.Opacity < 1);
        button.IsEnabled = true; Pump(); RecordButton("reenabled", button.IsEffectivelyEnabled && !button.IsPressed && root.Opacity == 1);
        if (palette == StudioPalette.Aquatic && dark)
        {
            using var frame = buttonHost.CaptureRenderedFrame()!;
            using var stream = File.Create(Path.Combine(output, consumer + "-Aquatic-Dark.png"));
            frame.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }

        void RecordButton(string state, bool condition)
        {
            var background = (ISolidColorBrush)root.Background!;
            var texts = button.GetVisualDescendants().OfType<TextBlock>().ToArray();
            var icons = button.GetVisualDescendants().OfType<IconGlyph>().ToArray();
            var backgroundKey = role == "primary" ? state == "pressed" ? "StudioPrimaryPressed" : state == "hover" ? "StudioPrimaryHover" : "StudioPrimary"
                : state is "hover" or "pressed" ? "SemiColorDangerLightActive" : "SemiColorDangerLight";
            var foregroundKey = role == "primary" ? "StudioOnPrimary" : state == "pressed" ? "SemiColorDangerActive" : "SemiColorDanger";
            button.TryFindResource(backgroundKey, button.ActualThemeVariant, out var expectedBackground);
            button.TryFindResource(foregroundKey, button.ActualThemeVariant, out var expectedForeground);
            var foregrounds = texts.Select(item => ((ISolidColorBrush)item.Foreground!).Color).ToArray();
            var iconForegrounds = icons.Select(item => ((ISolidColorBrush)item.Foreground!).Color).ToArray();
            var ratio = foregrounds.Min(value => Contrast(value, background.Color));
            var iconRatio = iconForegrounds.Length == 0 ? (double?)null : iconForegrounds.Min(value => Contrast(value, background.Color));
            var variant = palette + " " + (dark ? "Dark" : "Light");
            buttonColors.Add(new ButtonColorEvidence(consumer, role, variant, state, foregrounds.Select(item => item.ToString()).ToArray(),
                iconForegrounds.Select(item => item.ToString()).ToArray(), background.Color.ToString(), root.Opacity, ratio, iconRatio));
            if (!condition || texts.Length == 0 || expectedBackground is not ISolidColorBrush expectedBg || background.Color != expectedBg.Color ||
                expectedForeground is not ISolidColorBrush expectedFg || state != "disabled" && (foregrounds.Any(item => item != expectedFg.Color) || iconForegrounds.Any(item => item != expectedFg.Color)) ||
                state != "disabled" && (!button.IsEffectivelyEnabled || ratio < 4.5 || iconRatio < 3))
                throw new InvalidOperationException($"Button state failed: {consumer} {variant} {state}; ratio={ratio:F2}; button={button.Foreground}; text={string.Join(',', foregrounds)}; icons={string.Join(',', iconForegrounds)}; bg={background.Color}; expected={expectedForeground}/{expectedBackground}; condition={condition}; enabled={button.IsEnabled}/{button.IsEffectivelyEnabled}; opacity={root.Opacity}");
            buttonAssertions.Add(consumer + " " + variant + " " + state);
            Console.WriteLine("PASS Button " + buttonAssertions[^1] + $"; ratio={ratio:F2}");
        }
    }
    buttonHost.Close();
}
File.WriteAllText(Path.Combine(output, "button-colors.json"), JsonSerializer.Serialize(buttonColors, CheckboxJsonContext.Default.ListButtonColorEvidence));
File.WriteAllLines(Path.Combine(output, "button-assertions.txt"), buttonAssertions);
Console.WriteLine($"Button palette verification passed: {buttonAssertions.Count} assertions; NativeAOT={!RuntimeFeature.IsDynamicCodeSupported}; {output}");
return 0;

void Pump() { var timer = Stopwatch.StartNew(); do { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(8); } while (timer.ElapsedMilliseconds < 120); }
static double Contrast(Color a, Color b)
{
    static double Channel(byte value) { var x = value / 255d; return x <= .04045 ? x / 12.92 : Math.Pow((x + .055) / 1.055, 2.4); }
    static double Luminance(Color value) => .2126 * Channel(value.R) + .7152 * Channel(value.G) + .0722 * Channel(value.B);
    return (Math.Max(Luminance(a), Luminance(b)) + .05) / (Math.Min(Luminance(a), Luminance(b)) + .05);
}
sealed record CheckboxColorEvidence(string Variant, string State, string Foreground, string Background, double GlyphOpacity, double BoxOpacity, double Ratio);
sealed record ButtonColorEvidence(string Consumer, string Role, string Variant, string State, string[] Foregrounds, string[] IconForegrounds, string Background, double RootOpacity, double Ratio, double? IconRatio);
// 测试命令只恢复控件可用性；状态检查移出后释放鼠标，不执行任何产品命令。
sealed class FixtureCommand : System.Windows.Input.ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => throw new InvalidOperationException("State probes must not invoke commands.");
}
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(List<CheckboxColorEvidence>))]
[JsonSerializable(typeof(List<ButtonColorEvidence>))]
partial class CheckboxJsonContext : JsonSerializerContext;
