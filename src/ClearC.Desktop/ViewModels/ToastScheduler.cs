using Avalonia.Threading;

namespace ClearC.Desktop.ViewModels;

/// <summary>Toast 自动消失的定时器抽象，便于单元测试替换。</summary>
internal interface IToastScheduler
{
    void Schedule(TimeSpan delay, Action callback);

    void Cancel();
}

internal sealed class DispatcherToastScheduler : IToastScheduler
{
    private DispatcherTimer? _timer;

    public void Schedule(TimeSpan delay, Action callback)
    {
        Cancel();
        var timer = new DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) =>
        {
            Cancel();
            callback();
        };
        _timer = timer;
        timer.Start();
    }

    public void Cancel()
    {
        _timer?.Stop();
        _timer = null;
    }
}
