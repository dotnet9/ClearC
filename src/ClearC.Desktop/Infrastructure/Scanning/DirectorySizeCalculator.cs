using System.IO.Enumeration;

namespace ClearC.Desktop.Infrastructure.Scanning;

/// <summary>
/// 目录大小统计。
/// 走 <see cref="FileSystemEnumerable{TResult}"/>：一次目录枚举就带回类型、大小与写入时间，
/// 不再对每个条目额外调用 <c>File.GetAttributes</c> / <c>new FileInfo(...)</c>（三者合一会快数倍）。
/// </summary>
internal sealed class DirectorySizeCalculator : IDirectorySizeCalculator
{
    /// <summary>
    /// 只跳过重解析点（符号链接 / 目录联接）。
    /// 注意不能沿用默认的 <c>Hidden | System</c>：临时目录与休眠/页面文件恰恰是隐藏或系统文件，
    /// 默认值会漏统计。
    /// </summary>
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        ReturnSpecialDirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

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
                // 单文件目标（WSL/Docker 的 ext4.vhdx、hiberfil.sys 等）。
                try
                {
                    var file = new FileInfo(path);
                    Count(file.FullName, file.Length, new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero));
                }
                catch (Exception exception) when (IsExpectedFileSystemException(exception))
                {
                    // 文件在扫描过程中被删除：跳过。
                }
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
                var entries = new FileSystemEnumerable<ScannedEntry>(directory, Select, Options);
                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (entry.IsDirectory)
                    {
                        pendingDirectories.Push(entry.Path);
                    }
                    else
                    {
                        Count(entry.Path, entry.Length, entry.LastWriteTimeUtc);
                    }
                }
            }
            catch (PathTooLongException)
            {
                tooLongPathFiles++;
            }
            catch (Exception exception) when (IsExpectedFileSystemException(exception))
            {
                // 目录不可枚举（权限/占用/已删除）：跳过该目录，不影响其它目标。
            }
        }

        return new(bytes, fileCount, lastWriteTimeUtc, tooLongPathFiles);

        void Count(string path, long length, DateTimeOffset written)
        {
            try
            {
                if (!Matches(path, written))
                {
                    return;
                }

                bytes = checked(bytes + length);
                fileCount++;
                if (lastWriteTimeUtc is null || written > lastWriteTimeUtc)
                {
                    lastWriteTimeUtc = written;
                }
            }
            catch (OverflowException)
            {
                bytes = long.MaxValue;
            }
            catch (Exception exception) when (IsExpectedFileSystemException(exception))
            {
                // 条目在枚举与读取之间消失：跳过。
            }
        }

        bool Matches(string path, DateTimeOffset written)
        {
            if (request.IncludePatterns is { Count: > 0 } patterns &&
                !patterns.Any(pattern => FileSystemName.MatchesSimpleExpression(pattern, Path.GetFileName(path), true)))
            {
                return false;
            }

            return request.ModifiedBefore is null || written.UtcDateTime < request.ModifiedBefore.Value.UtcDateTime;
        }
    }

    /// <summary>枚举转换器：只在文件上取大小与写入时间（目录上这两个字段无意义）。</summary>
    private static ScannedEntry Select(ref FileSystemEntry entry) => entry.IsDirectory
        ? new(true, entry.ToFullPath(), 0, default)
        : new(false, entry.ToFullPath(), entry.Length, entry.LastWriteTimeUtc);

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

    private readonly record struct ScannedEntry(
        bool IsDirectory,
        string Path,
        long Length,
        DateTimeOffset LastWriteTimeUtc);
}
