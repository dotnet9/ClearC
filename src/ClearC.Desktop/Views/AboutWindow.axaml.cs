using ClearC.Desktop.ViewModels;
using CodeWF.Avalonia.Controls.Controls;

namespace ClearC.Desktop.Views;

public sealed partial class AboutWindow : CodeWFWindow
{
    public AboutWindow() => InitializeComponent();

    protected override void OnClosed(EventArgs args)
    {
        if (DataContext is AboutViewModel viewModel) viewModel.Update.Cancel();
        base.OnClosed(args);
    }
}
