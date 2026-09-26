using System.Runtime.InteropServices;
using ClearC.Desktop.Infrastructure.Windows;

namespace ClearC.Desktop.Infrastructure.Cleanup;

internal interface IRecycleBinCleaner
{
    Task<RecycleBinCleanupResult> EmptyAsync(string driveRoot, CancellationToken cancellationToken = default);
}

internal sealed record RecycleBinCleanupResult(bool Succeeded, string Error = "");

internal sealed class RecycleBinCleaner : IRecycleBinCleaner
{
    private const uint NoConfirmation = 0x00000001;
    private const uint NoProgressUi = 0x00000002;
    private const uint NoSound = 0x00000004;

    private readonly IPlatform _platform;

    public RecycleBinCleaner()
        : this(new SystemPlatform())
    {
    }

    internal RecycleBinCleaner(IPlatform platform) => _platform = platform;

    public Task<RecycleBinCleanupResult> EmptyAsync(
        string driveRoot,
        CancellationToken cancellationToken = default) => Task.Run(
        () => Empty(driveRoot),
        cancellationToken);

    private RecycleBinCleanupResult Empty(string driveRoot)
    {
        if (!_platform.IsWindows)
        {
            return new(false, "仅支持 Windows。");
        }

        var result = SHEmptyRecycleBin(IntPtr.Zero, driveRoot, NoConfirmation | NoProgressUi | NoSound);
        return result == 0
            ? new(true)
            : new(false, $"Windows 返回错误 0x{result:X8}。");
    }

    [DllImport("shell32.dll", EntryPoint = "SHEmptyRecycleBinW", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr windowHandle, string rootPath, uint flags);
}
