using Avalonia.Controls;
using ClearC.Desktop.ViewModels;

namespace ClearC.Desktop.Views;

public sealed partial class MainWindow : Window
{
    private bool _closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
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
