namespace ClearC.Core.Models;

public sealed record ScanResult(
    DiskSnapshot Disk,
    IReadOnlyList<CleanupItem> Items,
    TimeSpan Elapsed)
{
    public long TotalBytes => Items.Sum(item => item.SizeBytes);

    public long RecommendedBytes => Items.Where(item => item.IsRecommended).Sum(item => item.SizeBytes);
}
