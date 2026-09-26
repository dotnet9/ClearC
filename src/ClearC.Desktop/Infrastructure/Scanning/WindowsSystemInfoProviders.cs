using System.Runtime.InteropServices;
using ClearC.Core.Models;
using ClearC.Desktop.Infrastructure.Windows;

namespace ClearC.Desktop.Infrastructure.Scanning;

internal interface IDiskInfoProvider
{
    DiskSnapshot GetSystemDrive();
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
