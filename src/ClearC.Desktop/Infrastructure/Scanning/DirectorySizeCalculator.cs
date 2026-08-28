namespace ClearC.Desktop.Infrastructure.Scanning;

internal sealed class DirectorySizeCalculator : IDirectorySizeCalculator
{
    public Task<DirectorySize> CalculateAsync(
        IReadOnlyList<string> paths,
        DateTimeOffset? modifiedBefore = null,
        CancellationToken cancellationToken = default) => Task.Run(
        () => Calculate(paths, modifiedBefore, cancellationToken),
        cancellationToken);

    private static DirectorySize Calculate(
        IReadOnlyList<string> paths,
        DateTimeOffset? modifiedBefore,
        CancellationToken cancellationToken)
    {
        long bytes = 0;
        long fileCount = 0;
        var pendingDirectories = new Stack<string>();

        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(path))
            {
                AddFile(path, modifiedBefore, ref bytes, ref fileCount);
            }
            else if (Directory.Exists(path))
            {
                pendingDirectories.Push(path);
            }
        }

        while (pendingDirectories.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var attributes = File.GetAttributes(entry);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            continue;
                        }

                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            pendingDirectories.Push(entry);
                        }
                        else
                        {
                            AddFile(entry, modifiedBefore, ref bytes, ref fileCount);
                        }
                    }
                    catch (Exception exception) when (IsExpectedFileSystemException(exception))
                    {
                        // A changing or protected cache entry should not abort the complete scan.
                    }
                }
            }
            catch (Exception exception) when (IsExpectedFileSystemException(exception))
            {
                // Continue with the remaining targets when a directory cannot be enumerated.
            }
        }

        return new(bytes, fileCount);
    }

    private static void AddFile(
        string path,
        DateTimeOffset? modifiedBefore,
        ref long bytes,
        ref long fileCount)
    {
        try
        {
            var file = new FileInfo(path);
            if (modifiedBefore is not null && file.LastWriteTimeUtc >= modifiedBefore.Value.UtcDateTime)
            {
                return;
            }

            bytes = checked(bytes + file.Length);
            fileCount++;
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
        }
        catch (OverflowException)
        {
            bytes = long.MaxValue;
        }
    }

    private static bool IsExpectedFileSystemException(Exception exception) => exception is
        UnauthorizedAccessException or
        IOException or
        FileNotFoundException or
        DirectoryNotFoundException or
        PathTooLongException or
        NotSupportedException;
}
