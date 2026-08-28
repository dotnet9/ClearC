using ClearC.Core.Models;

namespace ClearC.Core.Services;

public interface ICleanupScanner
{
    Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default);
}
