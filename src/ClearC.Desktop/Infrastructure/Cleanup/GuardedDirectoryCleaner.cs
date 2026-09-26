namespace ClearC.Desktop.Infrastructure.Cleanup;

internal readonly record struct DirectoryCleanupResult(
    long FreedBytes,
    long DeletedFiles,
    long SkippedFiles,
    long LeftoverDirectories = 0);

internal sealed record DirectoryCleanupRequest(
    IReadOnlyList<string> ApprovedRoots,
    DateTimeOffset? ModifiedBefore = null,
    IReadOnlyList<string>? IncludePatterns = null,
    bool DeleteEmptyDirectories = true);

internal interface IGuardedDirectoryCleaner
{
    Task<DirectoryCleanupResult> CleanContentsAsync(
        DirectoryCleanupRequest request,
        CancellationToken cancellationToken);
}

internal sealed class GuardedDirectoryCleaner : IGuardedDirectoryCleaner
{
    public Task<DirectoryCleanupResult> CleanContentsAsync(
        DirectoryCleanupRequest request,
        CancellationToken cancellationToken) => Task.Run(
        () => CleanContents(request, cancellationToken),
        cancellationToken);

    private static DirectoryCleanupResult CleanContents(
        DirectoryCleanupRequest request,
        CancellationToken cancellationToken)
    {
        long freedBytes = 0;
        long deletedFiles = 0;
        long skippedFiles = 0;
        long leftoverDirectories = 0;

        foreach (var root in request.ApprovedRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalizedRoot = ValidateRoot(root);
            if (!Directory.Exists(normalizedRoot))
            {
                continue;
            }

            var directories = new Stack<(string Path, int Depth)>();
            var visitedDirectories = new List<(string Path, int Depth)>();
            directories.Push((normalizedRoot, 0));

            while (directories.TryPop(out var current))
            {
                cancellationToken.ThrowIfCancellationRequested();
                visitedDirectories.Add(current);
                try
                {
                    foreach (var entry in Directory.EnumerateFileSystemEntries(current.Path))
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
                                directories.Push((entry, current.Depth + 1));
                            }
                            else if (ShouldDelete(entry, request))
                            {
                                DeleteFile(entry, request.ModifiedBefore, ref freedBytes, ref deletedFiles, ref skippedFiles);
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

            if (request.DeleteEmptyDirectories)
            {
                // 显式深度排序：先删最深的目录，浅层空目录才有机会随子目录一起被移除。
                foreach (var directory in visitedDirectories
                             .Where(entry => entry.Depth > 0)
                             .OrderByDescending(entry => entry.Depth)
                             .ThenByDescending(entry => entry.Path.Length))
                {
                    if (!TryDeleteEmptyDirectory(directory.Path))
                    {
                        leftoverDirectories++;
                    }
                }
            }
        }

        return new(freedBytes, deletedFiles, skippedFiles, leftoverDirectories);
    }

    private static bool ShouldDelete(string path, DirectoryCleanupRequest request) =>
        request.IncludePatterns is not { Count: > 0 } patterns ||
        patterns.Any(pattern => System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(
            pattern,
            Path.GetFileName(path),
            ignoreCase: true));

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

    private static bool TryDeleteEmptyDirectory(string path)
    {
        try
        {
            // 目录非空时 Directory.Delete(..., false) 抛 IOException，说明还有内容需要保留。
            Directory.Delete(path, false);
            return true;
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            return false;
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
