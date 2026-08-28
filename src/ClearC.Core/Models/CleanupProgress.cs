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
    string Message);

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

    public int CompletedCount => Items.Count(item => item.Outcome == CleanupOutcome.Completed);
}
