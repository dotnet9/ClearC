using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;

namespace ClearC.Desktop.Controls;

/// <summary>
/// 磁盘占用环图。逻辑尺寸 150×150、半径 56、线宽 9、起始 -90°、圆角端点；
/// 进度变化按 1s 缓动补间（原型 <c>cubic-bezier(.3,.7,.3,1)</c>）。
/// </summary>
public sealed class DiskDonut : Control
{
    /// <summary>半径相对控件短边的比例（56 / 150）。</summary>
    private const double RadiusRatio = 56.0 / 150.0;

    public static readonly StyledProperty<double> UsedRatioProperty = AvaloniaProperty.Register<DiskDonut, double>(
        nameof(UsedRatio),
        0,
        validate: value => value is >= 0 and <= 1);

    public static readonly StyledProperty<double> AnimatedRatioProperty = AvaloniaProperty.Register<DiskDonut, double>(
        nameof(AnimatedRatio));

    public static readonly StyledProperty<IBrush?> TrackBrushProperty = AvaloniaProperty.Register<DiskDonut, IBrush?>(
        nameof(TrackBrush));

    public static readonly StyledProperty<IBrush?> AccentBrushProperty = AvaloniaProperty.Register<DiskDonut, IBrush?>(
        nameof(AccentBrush));

    public static readonly StyledProperty<double> StrokeThicknessProperty = AvaloniaProperty.Register<DiskDonut, double>(
        nameof(StrokeThickness),
        9);

    static DiskDonut()
    {
        AffectsRender<DiskDonut>(AnimatedRatioProperty, TrackBrushProperty, AccentBrushProperty, StrokeThicknessProperty);
        UsedRatioProperty.Changed.AddClassHandler<DiskDonut>((donut, args) =>
            donut.SetCurrentValue(AnimatedRatioProperty, (double)(args.NewValue ?? 0d)));
    }

    public DiskDonut()
    {
        Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = AnimatedRatioProperty,
                Duration = TimeSpan.FromSeconds(1),
                Easing = new CubicEaseOut()
            }
        };
    }

    public double UsedRatio
    {
        get => GetValue(UsedRatioProperty);
        set => SetValue(UsedRatioProperty, value);
    }

    public double AnimatedRatio => GetValue(AnimatedRatioProperty);

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public IBrush? AccentBrush
    {
        get => GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var size = Math.Min(Bounds.Width, Bounds.Height);
        var thickness = StrokeThickness;
        var radius = size / 2 * RadiusRatio;
        if (radius <= thickness)
        {
            return;
        }

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var track = TrackBrush ?? Brushes.LightGray;
        context.DrawEllipse(null, new Pen(track, thickness), center, radius, radius);

        var ratio = Math.Clamp(AnimatedRatio, 0, 1);
        if (ratio <= 0)
        {
            return;
        }

        var accent = AccentBrush;
        if (accent is null)
        {
            return;
        }

        // 满环时用整圆，避免 ArcTo 起点与终点重合导致整段不渲染。
        if (ratio >= 0.9999)
        {
            context.DrawEllipse(null, new Pen(accent, thickness), center, radius, radius);
            return;
        }

        var sweep = ratio * Math.PI * 2;
        var startAngle = -Math.PI / 2;
        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(PointOnCircle(center, radius, startAngle), false);
            geometryContext.ArcTo(
                PointOnCircle(center, radius, startAngle + sweep),
                new Size(radius, radius),
                0,
                sweep > Math.PI,
                SweepDirection.Clockwise);
        }

        context.DrawGeometry(null, new Pen(accent, thickness, lineCap: PenLineCap.Round), geometry);
    }

    private static Point PointOnCircle(Point center, double radius, double angle) => new(
        center.X + radius * Math.Cos(angle),
        center.Y + radius * Math.Sin(angle));
}
