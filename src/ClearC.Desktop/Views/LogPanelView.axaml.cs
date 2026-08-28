using System.Collections.Specialized;
using Avalonia.Controls;
using ClearC.Desktop.ViewModels;

namespace ClearC.Desktop.Views;

public sealed partial class LogPanelView : UserControl
{
    private LogPanelViewModel? _viewModel;

    public LogPanelView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.Logs.CollectionChanged -= Logs_OnCollectionChanged;
        }

        _viewModel = DataContext as LogPanelViewModel;
        if (_viewModel is not null)
        {
            _viewModel.Logs.CollectionChanged += Logs_OnCollectionChanged;
        }
    }

    private void Logs_OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        LogScroller.ScrollToEnd();
}
