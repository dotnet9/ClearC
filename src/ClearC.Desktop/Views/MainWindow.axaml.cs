using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ClearC.Desktop.ViewModels;

namespace ClearC.Desktop.Views;

public sealed partial class MainWindow : Window
{
    private bool _closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        // 产品口径：非 Windows 平台功能正在开发中，启动时给出友好提示（不阻塞浏览界面）
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            PlatformNoticeBanner.IsVisible = true;
        }
    }

    private void OnPlatformNoticeDismiss(object? sender, RoutedEventArgs e)
    {
        PlatformNoticeBanner.IsVisible = false;
    }

    /// <summary>Esc 关闭当前模态（确认页 / 关闭保护），减少鼠标折返。</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape &&
            DataContext is MainWindowViewModel viewModel &&
            viewModel.TryDismissModal())
        {
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // 扫描/清理进行中关闭会造成半清理，先让用户确认（§3.2）。
        if (!_closeConfirmed && DataContext is MainWindowViewModel viewModel && CloseGuard.RequiresConfirmation(viewModel.State))
        {
            e.Cancel = true;
            viewModel.RequestCloseConfirmation();
            return;
        }

        if (DataContext is MainWindowViewModel current)
        {
            current.StopToastTimer();
        }

        base.OnClosing(e);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CloseRequested += OnCloseRequested;
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        if (sender is MainWindowViewModel viewModel)
        {
            viewModel.CloseRequested -= OnCloseRequested;
        }

        _closeConfirmed = true;
        Close();
    }
}
