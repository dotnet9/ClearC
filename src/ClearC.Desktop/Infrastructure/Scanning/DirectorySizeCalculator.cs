using System.IO.Enumeration;

namespace ClearC.Desktop.Infrastructure.Scanning;

internal sealed class DirectorySizeCalculator : IDirectorySizeCalculator
{
    public Task<DirectorySize> CalculateAsync(
        DirectorySizeRequest request,
        CancellationToken cancellationToken = default) => Task.Run(
        () => Calculate(request, cancellationToken),
        cancellationToken);

    private static DirectorySize Calculate(DirectorySizeRequest request, CancellationToken cancellationToken)
    {
        long bytes = 0;
        long fileCount = 0;
        long tooLongPathFiles = 0;
        DateTimeOffset? lastWriteTimeUtc = null;
        var pendingDirectories = new Stack<string>();
        var excluded = (request.ExcludedPaths ?? [])
            .Select(Normalize)
            .Where(path => path.Length > 0)
            .ToArray();

        foreach (var path in request.Paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(path))
            {
                AddFile(path, request, ref bytes, ref fileCount, ref lastWriteTimeUtc, ref tooLongPathFiles);
            }
            else if (Directory.Exists(path))
            {
                pendingDirectories.Push(path);
            }
        }

        while (pendingDirectories.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsExcluded(directory, excluded))
            {
                continue;
            }

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
                            AddFile(entry, request, ref bytes, ref fileCount, ref lastWriteTimeUtc, ref tooLongPathFiles);
                        }
                    }
                    catch (PathTooLongException)
                    {
                        tooLongPathFiles++;
                    }
                    catch (Exception exception) when (IsExpectedFileSystemException(exception))
                    {
                        // A changing or protected cache entry should not abort the complete scan.
                    }
                }
            }
            catch (PathTooLongException)
            {
                tooLongPathFiles++;
            }
            catch (Exception exception) when (IsExpectedFileSystemException(exception))
            {
                // Continue with the remaining targets when a directory cannot be enumerated.
            }
        }

        return new(bytes, fileCount, lastWriteTimeUtc, tooLongPathFiles);
    }

    private static void AddFile(
        string path,
        DirectorySizeRequest request,
        ref long bytes,
        ref long fileCount,
        ref DateTimeOffset? lastWriteTimeUtc,
        ref long tooLongPathFiles)
    {
        try
        {
            if (request.IncludePatterns is { Count: > 0 } patterns &&
                !patterns.Any(pattern => FileSystemName.MatchesSimpleExpression(pattern, Path.GetFileName(path), true)))
            {
                return;
            }

            var file = new FileInfo(path);
            if (request.ModifiedBefore is not null && file.LastWriteTimeUtc >= request.ModifiedBefore.Value.UtcDateTime)
            {
                return;
            }

            bytes = checked(bytes + file.Length);
            fileCount++;
            var written = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);
            if (lastWriteTimeUtc is null || written > lastWriteTimeUtc)
            {
                lastWriteTimeUtc = written;
            }
        }
        catch (PathTooLongException)
        {
            tooLongPathFiles++;
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
        }
        catch (OverflowException)
        {
            bytes = long.MaxValue;
        }
    }

    private static bool IsExcluded(string directory, IReadOnlyList<string> excluded) =>
        excluded.Any(path => IsSameOrChild(directory, path));

    private static bool IsSameOrChild(string candidate, string parent)
    {
        if (string.Equals(candidate, parent, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return candidate.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            return string.Empty;
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static bool IsExpectedFileSystemException(Exception exception) => exception is
        UnauthorizedAccessException or
        IOException or
        FileNotFoundException or
        DirectoryNotFoundException or
        NotSupportedException;
}
