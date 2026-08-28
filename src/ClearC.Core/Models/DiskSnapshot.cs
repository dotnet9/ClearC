namespace ClearC.Core.Models;

public sealed record DiskSnapshot(
    string DriveName,
    string DriveFormat,
    long TotalBytes,
    long FreeBytes)
{
    public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);

    public double UsedRatio => TotalBytes <= 0 ? 0 : (double)UsedBytes / TotalBytes;
}
