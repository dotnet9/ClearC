namespace ClearC.Desktop.Infrastructure.Scanning;

internal sealed record DirectorySizeRequest(
    IReadOnlyList<string> Paths,
    DateTimeOffset? ModifiedBefore = null,
    IReadOnlyList<string>? IncludePatterns = null,
    IReadOnlyList<string>? ExcludedPaths = null);

internal interface IDirectorySizeCalculator
{
    Task<DirectorySize> CalculateAsync(
        DirectorySizeRequest request,
        CancellationToken cancellationToken = default);
}

internal readonly record struct DirectorySize(
    long Bytes,
    long FileCount,
    DateTimeOffset? LastWriteTimeUtc = null,
    long TooLongPathFiles = 0);
