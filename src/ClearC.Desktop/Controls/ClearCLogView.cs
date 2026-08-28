using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CodeWF.Log.Avalonia;
using CodeWF.Log.Core;

namespace ClearC.Desktop.Controls;

public sealed class ClearCLogView : LogView
{
    private static readonly Color CodeWfLightContentColor = Color.Parse("#262626");
    private static readonly IBrush ClearCDarkContentBrush = new SolidColorBrush(Color.Parse("#B9CDE6"));
    private IDisposable? _logSubscription;
    private Timer? _paletteTimer;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _paletteTimer = new Timer(
            _ => Dispatcher.UIThread.Post(ApplyDarkContentPalette),
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        _logSubscription = Logger.Events.Subscribe(_ => SchedulePaletteRefresh(), replayRecent: false);
        SchedulePaletteRefresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _logSubscription?.Dispose();
        _logSubscription = null;
        _paletteTimer?.Dispose();
        _paletteTimer = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void SchedulePaletteRefresh()
    {
        // LogView batches rendering at 100 ms; debounce past that refresh before recoloring new runs.
        _paletteTimer?.Change(TimeSpan.FromMilliseconds(150), Timeout.InfiniteTimeSpan);
    }

    private void ApplyDarkContentPalette()
    {
        var textView = this.GetVisualDescendants().OfType<SelectableTextBlock>().FirstOrDefault();
        if (textView?.Inlines is null)
        {
            return;
        }

        foreach (var run in textView.Inlines.OfType<Run>())
        {
            if (run.Foreground is ISolidColorBrush { Color: var color } && color == CodeWfLightContentColor)
            {
                run.Foreground = ClearCDarkContentBrush;
            }
        }
    }
}
