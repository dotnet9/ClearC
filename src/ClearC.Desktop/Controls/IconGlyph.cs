using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ClearC.Desktop.Controls;

/// <summary>
/// 矢量图标：把 24×24 的 <see cref="Data"/> 等比缩放进控件边界。
/// 描边型默认圆角端点/连接、颜色取 <see cref="Foreground"/>；<see cref="Filled"/> 时改为填充。
/// </summary>
public sealed class IconGlyph : Control
{
    private const string KeyPrefix = "ClearCIcon";

    public static readonly StyledProperty<Geometry?> DataProperty = AvaloniaProperty.Register<IconGlyph, Geometry?>(
        nameof(Data));

    /// <summary>图标键（如 <c>i-file</c>），解析为 <c>ClearCIconFile</c> 资源；<see cref="Data"/> 为空时生效。</summary>
    public static readonly StyledProperty<string?> IconKeyProperty = AvaloniaProperty.Register<IconGlyph, string?>(
        nameof(IconKey));

    public static readonly StyledProperty<double> StrokeThicknessProperty = AvaloniaProperty.Register<IconGlyph, double>(
        nameof(StrokeThickness),
        1.7);

    public static readonly StyledProperty<bool> FilledProperty = AvaloniaProperty.Register<IconGlyph, bool>(
        nameof(Filled));

    public static readonly StyledProperty<IBrush?> ForegroundProperty = AvaloniaProperty.Register<IconGlyph, IBrush?>(
        nameof(Foreground));

    static IconGlyph()
    {
        AffectsRender<IconGlyph>(DataProperty, IconKeyProperty, StrokeThicknessProperty, FilledProperty, ForegroundProperty);
        IconKeyProperty.Changed.AddClassHandler<IconGlyph>((icon, _) => icon.InvalidateVisual());
    }

    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public string? IconKey
    {
        get => GetValue(IconKeyProperty);
        set => SetValue(IconKeyProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public bool Filled
    {
        get => GetValue(FilledProperty);
        set => SetValue(FilledProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var geometry = Data ?? ResolveIcon(IconKey);
        var brush = Foreground;
        if (geometry is null || brush is null || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var source = geometry.Bounds;
        if (source.Width <= 0 || source.Height <= 0)
        {
            return;
        }

        // 描边会向两侧扩展，先按线宽留出内边距，避免端点被裁掉。
        var padding = Filled ? 0 : StrokeThickness;
        var available = Math.Max(1, Math.Min(Bounds.Width - padding, Bounds.Height - padding));
        var scale = available / Math.Max(source.Width, source.Height);
        var offsetX = (Bounds.Width - source.Width * scale) / 2 - source.X * scale;
        var offsetY = (Bounds.Height - source.Height * scale) / 2 - source.Y * scale;

        using var transform = context.PushTransform(
            Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offsetX, offsetY));

        if (Filled)
        {
            context.DrawGeometry(brush, null, geometry);
            return;
        }

        var pen = new Pen(brush, StrokeThickness / scale, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        context.DrawGeometry(null, pen, geometry);
    }

    /// <summary>把 <c>i-file</c> 之类的图标键映射到 <c>ClearCIconFile</c> 资源。</summary>
    private static Geometry? ResolveIcon(string? iconKey)
    {
        if (string.IsNullOrWhiteSpace(iconKey) || Application.Current is not { } application)
        {
            return null;
        }

        var name = iconKey.StartsWith("i-", StringComparison.Ordinal) ? iconKey[2..] : iconKey;
        if (name.Length == 0)
        {
            return null;
        }

        var key = KeyPrefix + char.ToUpperInvariant(name[0]) + name[1..];
        return application.TryGetResource(key, application.ActualThemeVariant, out var value) ? value as Geometry : null;
    }
}
