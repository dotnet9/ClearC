using System.ComponentModel;
using System.Diagnostics;
using ClearC.Desktop.I18n;

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
            return AppText.Get(Language.LocationNoPath);
        }

        if (!Path.IsPathFullyQualified(path))
        {
            return AppText.Get(Language.LocationNotAbsolute);
        }

        if (!OperatingSystem.IsWindows())
        {
            return AppText.Get(Language.LocationWindowsOnly);
        }

        var isFile = File.Exists(path);
        if (!isFile && !Directory.Exists(path))
        {
            return AppText.Get(Language.LocationMissing);
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
            return AppText.Format(Language.LocationOpenFailed, exception.Message);
        }
    }
}
