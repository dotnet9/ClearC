using System.Runtime.InteropServices;

namespace ClearC.Desktop.Infrastructure.Cleanup;

internal interface IRecycleBinCleaner
{
    bool Empty(string driveRoot, out string error);
}

internal sealed class RecycleBinCleaner : IRecycleBinCleaner
{
    private const uint NoConfirmation = 0x00000001;
    private const uint NoProgressUi = 0x00000002;
    private const uint NoSound = 0x00000004;

    public bool Empty(string driveRoot, out string error)
    {
        var result = SHEmptyRecycleBin(IntPtr.Zero, driveRoot, NoConfirmation | NoProgressUi | NoSound);
        error = result == 0 ? string.Empty : $"Windows 返回错误 0x{result:X8}。";
        return result == 0;
    }

    [DllImport("shell32.dll", EntryPoint = "SHEmptyRecycleBinW", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr windowHandle, string rootPath, uint flags);
}
