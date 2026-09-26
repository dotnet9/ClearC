using System.ComponentModel;
using System.Diagnostics;

namespace ClearC.Desktop.Infrastructure.Windows;

/// <summary>
/// 在资源管理器中定位清理路径（详情面板的"打开位置"）。
/// 打开成功返回 <c>null</c>，否则返回可直接显示给用户的原因。
/// </summary>
public interface ILocationOpener
{
    string? TryOpen(string path);
}

public sealed class LocationOpener : ILocationOpener
{
    public string? TryOpen(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "没有可打开的路径";
        }

        if (!Path.IsPathFullyQualified(path))
        {
            return "路径不是绝对路径，无法打开";
        }

        if (!OperatingSystem.IsWindows())
        {
            return "仅支持在 Windows 资源管理器中打开";
        }

        var isFile = File.Exists(path);
        if (!isFile && !Directory.Exists(path))
        {
            return "路径已不存在，可能已被清理";
        }

        try
        {
            // 文件用 /select 定位到父目录并选中；目录直接打开。
            var arguments = isFile ? $"/select,\"{path}\"" : $"\"{path}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
            return null;
        }
        catch (Exception exception) when (exception is
            Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException or PlatformNotSupportedException)
        {
            return $"打开失败：{exception.Message}";
        }
    }
}
