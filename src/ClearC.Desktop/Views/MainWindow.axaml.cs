using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ClearC.Desktop.ViewModels;

namespace ClearC.Desktop.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Logs.CollectionChanged += Logs_OnCollectionChanged;
        }
    }

    private void Logs_OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => LogScroller.ScrollToEnd();

    private void TitleBar_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximized();
        }
        else
        {
            BeginMoveDrag(e);
        }
    }

    private void Minimize_OnClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_OnClick(object? sender, RoutedEventArgs e) => ToggleMaximized();

    private void Close_OnClick(object? sender, RoutedEventArgs e) => Close();

    private void ToggleMaximized() => WindowState = WindowState == WindowState.Maximized
        ? WindowState.Normal
        : WindowState.Maximized;
}
