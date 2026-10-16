using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClearC.Desktop;
using ClearC.Desktop.Controls;
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
return 0;

void Pump() { var timer = Stopwatch.StartNew(); do { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(8); } while (timer.ElapsedMilliseconds < 120); }
static double Contrast(Color a, Color b)
{
    static double Channel(byte value) { var x = value / 255d; return x <= .04045 ? x / 12.92 : Math.Pow((x + .055) / 1.055, 2.4); }
    static double Luminance(Color value) => .2126 * Channel(value.R) + .7152 * Channel(value.G) + .0722 * Channel(value.B);
    return (Math.Max(Luminance(a), Luminance(b)) + .05) / (Math.Min(Luminance(a), Luminance(b)) + .05);
}
sealed record CheckboxColorEvidence(string Variant, string State, string Foreground, string Background, double GlyphOpacity, double BoxOpacity, double Ratio);
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(List<CheckboxColorEvidence>))]
partial class CheckboxJsonContext : JsonSerializerContext;
