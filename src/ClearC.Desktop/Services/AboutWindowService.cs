using Avalonia.Controls;
using ClearC.Desktop.ViewModels;
using ClearC.Desktop.Views;

namespace ClearC.Desktop.Services;

/// <summary>关于窗口的创建和复用集中在界面服务，视图模型只发出打开请求。</summary>
internal sealed class AboutWindowService(Window owner, UpdateViewModel updates)
{
    private AboutWindow? _window;

    public void Show()
    {
        if (_window is not null) { _window.Activate(); return; }
        var model = new AboutViewModel(updates);
        _window = new AboutWindow { DataContext = model };
        model.RequestClose += (_, _) => _window?.Close();
        _window.Closed += (_, _) => _window = null;
        _window.Show(owner);
    }
}
