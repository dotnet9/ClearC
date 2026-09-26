using System.Diagnostics;
using System.Security.Principal;

namespace ClearC.Desktop.Infrastructure.Windows;

internal interface IElevationProbe
{
    bool IsElevated { get; }
}

internal interface IProcessStarter
{
    /// <summary>以管理员身份启动自身；用户拒绝 UAC 或启动失败时返回 false。</summary>
    bool StartElevated(string fileName);
}

internal interface IElevationService
{
    bool IsElevated { get; }

    /// <summary>按需提权：以管理员重启当前程序。返回 false 表示用户取消或启动失败。</summary>
    bool RestartElevated();
}

internal sealed class ElevationService : IElevationService
{
    private readonly IElevationProbe _probe;
    private readonly IProcessStarter _starter;

    public ElevationService()
        : this(new WindowsElevationProbe(), new ShellProcessStarter())
    {
    }

    internal ElevationService(IElevationProbe probe, IProcessStarter starter)
    {
        _probe = probe;
        _starter = starter;
    }

    public bool IsElevated => _probe.IsElevated;

    public bool RestartElevated()
    {
        if (_probe.IsElevated)
        {
            return false;
        }

        var executable = Environment.ProcessPath;
        return !string.IsNullOrWhiteSpace(executable) && _starter.StartElevated(executable);
    }
}

internal sealed class WindowsElevationProbe : IElevationProbe
{
    public bool IsElevated
    {
        get
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or InvalidOperationException or PlatformNotSupportedException)
            {
                return false;
            }
        }
    }
}

internal sealed class ShellProcessStarter : IProcessStarter
{
    public bool StartElevated(string fileName)
    {
        try
        {
            var startInfo = new ProcessStartInfo(fileName)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            };
            return Process.Start(startInfo) is not null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            // 用户拒绝 UAC：保持未提权状态，不弹二次提示。
            return false;
        }
    }
}
