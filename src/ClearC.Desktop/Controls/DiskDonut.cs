using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ClearC.Desktop.Controls;

public sealed class DiskDonut : Control
{
    public static readonly StyledProperty<double> UsedRatioProperty = AvaloniaProperty.Register<DiskDonut, double>(
        nameof(UsedRatio),
        0,
        validate: value => value is >= 0 and <= 1);

    static DiskDonut()
    {
        AffectsRender<DiskDonut>(UsedRatioProperty);
    }

    public double UsedRatio
    {
        get => GetValue(UsedRatioProperty);
        set => SetValue(UsedRatioProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = Math.Max(0, Math.Min(Bounds.Width, Bounds.Height) / 2 - 15);
        var accent = new SolidColorBrush(Color.Parse("#22D3EE"));
        var blue = new SolidColorBrush(Color.Parse("#3B82F6"));
        var track = new SolidColorBrush(Color.Parse("#1F334C"));
        var decoration = new SolidColorBrush(Color.Parse("#4D67E8F9"));

        for (var index = 0; index < 36; index++)
        {
            var angle = index * Math.PI * 2 / 36;
            var inner = PointOnCircle(center, radius + 12, angle);
            var outer = PointOnCircle(center, radius + (index % 3 == 0 ? 16 : 14), angle);
            context.DrawLine(new Pen(decoration, index % 3 == 0 ? 1.2 : 0.7), inner, outer);
        }

        context.DrawEllipse(null, new Pen(track, 9), center, radius, radius);
        if (UsedRatio <= 0)
        {
            return;
        }

        var sweep = Math.Min(UsedRatio, 0.9999) * Math.PI * 2;
        var startAngle = -Math.PI / 2;
        var start = PointOnCircle(center, radius, startAngle);
        var end = PointOnCircle(center, radius, startAngle + sweep);
        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(start, false);
            geometryContext.ArcTo(
                end,
                new Size(radius, radius),
                0,
                sweep > Math.PI,
                SweepDirection.Clockwise);
        }

        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(accent.Color, 0),
                new GradientStop(blue.Color, 1)
            }
        };
        context.DrawGeometry(null, new Pen(brush, 9, lineCap: PenLineCap.Round), geometry);
    }

    private static Point PointOnCircle(Point center, double radius, double angle) => new(
        center.X + radius * Math.Cos(angle),
        center.Y + radius * Math.Sin(angle));
}
