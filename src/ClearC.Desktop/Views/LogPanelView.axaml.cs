using System.Text;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ClearC.Desktop.ViewModels;

namespace ClearC.Desktop.Views;

public sealed partial class LogPanelView : UserControl
{
    public LogPanelView() => InitializeComponent();

    private void Copy_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LogPanelViewModel viewModel || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        _ = clipboard.SetTextAsync(viewModel.LogText);
    }

    private async void Export_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LogPanelViewModel viewModel || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出运行日志",
            SuggestedFileName = $"clearc-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            DefaultExtension = "txt",
            FileTypeChoices = [new FilePickerFileType("文本文件") { Patterns = ["*.txt"] }]
        });

        if (file is null)
        {
            return;
        }

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(viewModel.LogText);
    }
}
