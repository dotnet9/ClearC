namespace ClearC.Core.Models;

public sealed record ScanProgress(int Completed, int Total, string CurrentTarget)
{
    public double Ratio => Total <= 0 ? 0 : Math.Clamp((double)Completed / Total, 0, 1);
}
