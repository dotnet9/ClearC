namespace ClearC.Core.Models;

public enum CleanupOutcome
{
    Completed,
    Skipped,
    Failed,
    Cancelled
}

public sealed record CleanupItemResult(
    string ItemId,
    CleanupOutcome Outcome,
    long FreedBytes,
    string Message,
    bool IsEstimated = false);

public sealed record CleanupProgress(
    int Completed,
    int Total,
    CleanupItem Item,
    CleanupItemResult? Result = null)
{
    public double Ratio => Total <= 0 ? 0 : Math.Clamp((double)Completed / Total, 0, 1);
}

public sealed record CleanupResult(IReadOnlyList<CleanupItemResult> Items, TimeSpan Elapsed)
{
    public long FreedBytes => Items.Sum(item => item.FreedBytes);

    /// <summary>目录类清理实测得到的释放量。</summary>
    public long MeasuredFreedBytes => Items.Where(item => !item.IsEstimated).Sum(item => item.FreedBytes);

    /// <summary>官方命令清理按扫描时大小估算的释放量。</summary>
    public long EstimatedFreedBytes => Items.Where(item => item.IsEstimated).Sum(item => item.FreedBytes);

    public int CompletedCount => Items.Count(item => item.Outcome == CleanupOutcome.Completed);
}
