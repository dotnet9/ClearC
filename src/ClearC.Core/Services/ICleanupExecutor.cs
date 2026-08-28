using ClearC.Core.Models;

namespace ClearC.Core.Services;

public interface ICleanupExecutor
{
    Task<CleanupResult> CleanAsync(
        IReadOnlyList<CleanupItem> plan,
        IProgress<CleanupProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
