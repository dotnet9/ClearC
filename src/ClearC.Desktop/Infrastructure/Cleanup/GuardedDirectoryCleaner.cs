namespace ClearC.Desktop.Infrastructure.Cleanup;

internal readonly record struct DirectoryCleanupResult(long FreedBytes, long DeletedFiles, long SkippedFiles);

internal interface IGuardedDirectoryCleaner
{
    Task<DirectoryCleanupResult> CleanContentsAsync(
        IReadOnlyList<string> approvedRoots,
        DateTimeOffset? modifiedBefore,
        CancellationToken cancellationToken);
}

internal sealed class GuardedDirectoryCleaner : IGuardedDirectoryCleaner
{
    public Task<DirectoryCleanupResult> CleanContentsAsync(
        IReadOnlyList<string> approvedRoots,
        DateTimeOffset? modifiedBefore,
        CancellationToken cancellationToken) => Task.Run(
        () => CleanContents(approvedRoots, modifiedBefore, cancellationToken),
        cancellationToken);

    private static DirectoryCleanupResult CleanContents(
        IReadOnlyList<string> approvedRoots,
        DateTimeOffset? modifiedBefore,
        CancellationToken cancellationToken)
    {
        long freedBytes = 0;
        long deletedFiles = 0;
        long skippedFiles = 0;

        foreach (var root in approvedRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalizedRoot = ValidateRoot(root);
            if (!Directory.Exists(normalizedRoot))
            {
                continue;
            }

            var directories = new Stack<string>();
            var visitedDirectories = new List<string>();
            directories.Push(normalizedRoot);

            while (directories.TryPop(out var directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                visitedDirectories.Add(directory);
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
                                skippedFiles++;
                            }
                            else if ((attributes & FileAttributes.Directory) != 0)
                            {
                                directories.Push(entry);
                            }
                            else
                            {
                                DeleteFile(entry, modifiedBefore, ref freedBytes, ref deletedFiles, ref skippedFiles);
                            }
                        }
                        catch (Exception exception) when (IsExpectedFileSystemException(exception))
                        {
                            skippedFiles++;
                        }
                    }
                }
                catch (Exception exception) when (IsExpectedFileSystemException(exception))
                {
                    skippedFiles++;
                }
            }

            foreach (var directory in visitedDirectories
                         .Where(directory => !string.Equals(directory, normalizedRoot, StringComparison.OrdinalIgnoreCase))
                         .OrderByDescending(directory => directory.Length))
            {
                TryDeleteEmptyDirectory(directory);
            }
        }

        return new(freedBytes, deletedFiles, skippedFiles);
    }

    private static string ValidateRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
        {
            throw new InvalidOperationException("清理根目录必须是完整路径。");
        }

        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var pathRoot = Path.TrimEndingDirectorySeparator(Path.GetPathRoot(normalized) ?? string.Empty);
        if (string.Equals(normalized, pathRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("拒绝以磁盘根目录作为清理目标。");
        }

        return normalized;
    }

    private static void DeleteFile(
        string path,
        DateTimeOffset? modifiedBefore,
        ref long freedBytes,
        ref long deletedFiles,
        ref long skippedFiles)
    {
        try
        {
            var info = new FileInfo(path);
            if (modifiedBefore is not null && info.LastWriteTimeUtc >= modifiedBefore.Value.UtcDateTime)
            {
                return;
            }

            var length = info.Length;
            File.Delete(path);
            freedBytes += length;
            deletedFiles++;
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            skippedFiles++;
        }
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            Directory.Delete(path, false);
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
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
