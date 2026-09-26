namespace ClearC.Core.Models;

public sealed record ScanResult(
    DiskSnapshot Disk,
    IReadOnlyList<CleanupItem> Items,
    TimeSpan Elapsed)
{
    public long TotalBytes => Items.Sum(item => item.SizeBytes);
}
