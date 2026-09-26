using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace ClearC.Desktop.Views;

public sealed partial class TitleBarView : UserControl
{
    public TitleBarView() => InitializeComponent();

    private void TitleBar_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window window ||
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximized(window);
        }
        else
        {
            window.BeginMoveDrag(e);
        }
    }

    private void Minimize_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window window)
        {
            window.WindowState = WindowState.Minimized;
        }
    }

    private void Maximize_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window window)
        {
            ToggleMaximized(window);
        }
    }

    private void Close_OnClick(object? sender, RoutedEventArgs e) =>
        (TopLevel.GetTopLevel(this) as Window)?.Close();

    /// <summary>最大化时切换成还原图标（原型 <c>i-max</c> / <c>i-restore</c>）。</summary>
    private void ToggleMaximized(Window window)
    {
        var maximized = window.WindowState != WindowState.Maximized;
        window.WindowState = maximized ? WindowState.Maximized : WindowState.Normal;
        MaximizeIcon.Data = (Geometry?)Application.Current?.FindResource(
            maximized ? "ClearCIconRestore" : "ClearCIconMax");
    }
}
