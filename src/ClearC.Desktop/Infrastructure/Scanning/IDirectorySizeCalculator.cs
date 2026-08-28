namespace ClearC.Desktop.Infrastructure.Scanning;

internal interface IDirectorySizeCalculator
{
    Task<DirectorySize> CalculateAsync(
        IReadOnlyList<string> paths,
        DateTimeOffset? modifiedBefore = null,
        CancellationToken cancellationToken = default);
}

internal readonly record struct DirectorySize(long Bytes, long FileCount);
