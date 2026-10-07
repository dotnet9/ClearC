using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;

namespace ClearC.Desktop.Infrastructure.Windows;

/// <summary>系统剪贴板写入（右键菜单的"复制完整路径"）。</summary>
public interface ITextClipboard
{
    Task CopyAsync(string text);
}

public sealed class DesktopTextClipboard : ITextClipboard
{
    public async Task CopyAsync(string text)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime
            {
                MainWindow: { } window
            })
        {
            await window.Clipboard.SetTextAsync(text);
        }
    }
}
