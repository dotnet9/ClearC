using System.Runtime.InteropServices;
using ClearC.Core.Models;

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
    DirectorySize GetInfo(string driveRoot);
}

internal sealed class WindowsRecycleBinInfoProvider : IRecycleBinInfoProvider
{
    public DirectorySize GetInfo(string driveRoot)
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
