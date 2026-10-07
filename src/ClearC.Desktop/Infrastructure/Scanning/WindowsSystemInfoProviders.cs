using System.Runtime.InteropServices;
using ClearC.Core.Models;
using ClearC.Desktop.Infrastructure.Windows;

namespace ClearC.Desktop.Infrastructure.Scanning;

internal interface IDiskInfoProvider
{
    DiskSnapshot GetSystemDrive();

    /// <summary>本机全部固定磁盘（系统盘优先），供盘符选择条与多盘扫描使用。</summary>
    IReadOnlyList<DiskSnapshot> GetFixedDrives();
}

internal sealed class WindowsDiskInfoProvider : IDiskInfoProvider
{
    public DiskSnapshot GetSystemDrive()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var root = Path.GetPathRoot(windows) ?? @"C:\";
        var drive = new DriveInfo(root);
        return new(drive.Name.TrimEnd('\\'), drive.DriveFormat, drive.TotalSize, drive.AvailableFreeSpace);
    }

    public IReadOnlyList<DiskSnapshot> GetFixedDrives()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [GetSystemDrive()];
        }

        var systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? @"C:\";
        var snapshots = new List<DiskSnapshot>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
                {
                    continue;
                }

                string? label = null;
                try
                {
                    label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? null : drive.VolumeLabel;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }

                snapshots.Add(new(
                    drive.Name.TrimEnd('\\'),
                    drive.DriveFormat,
                    drive.TotalSize,
                    drive.AvailableFreeSpace,
                    label));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        // 系统盘排在首位，其余按盘符排序。
        return snapshots
            .OrderByDescending(snapshot => snapshot.DriveName.Equals(systemRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            .ThenBy(snapshot => snapshot.DriveName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

internal interface IRecycleBinInfoProvider
{
    Task<DirectorySize> GetInfoAsync(string driveRoot, CancellationToken cancellationToken = default);
}

internal sealed class WindowsRecycleBinInfoProvider : IRecycleBinInfoProvider
{
    private readonly IPlatform _platform;

    public WindowsRecycleBinInfoProvider()
        : this(SystemPlatform.Instance)
    {
    }

    internal WindowsRecycleBinInfoProvider(IPlatform platform) => _platform = platform;

    public Task<DirectorySize> GetInfoAsync(string driveRoot, CancellationToken cancellationToken = default) =>
        _platform.IsWindows
            ? Task.Run(() => Query(driveRoot), cancellationToken)
            : Task.FromResult(default(DirectorySize));

    private static DirectorySize Query(string driveRoot)
    {
        var info = new ShQueryRbInfo { Size = Marshal.SizeOf<ShQueryRbInfo>() };
        return SHQueryRecycleBin(driveRoot, ref info) == 0
            ? new(info.Bytes, info.ItemCount)
            : default;
    }

    [DllImport("shell32.dll", EntryPoint = "SHQueryRecycleBinW", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string rootPath, ref ShQueryRbInfo info);

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct ShQueryRbInfo
    {
        public int Size;
        public long Bytes;
        public long ItemCount;
    }
}
