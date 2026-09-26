using System.IO.Enumeration;

namespace ClearC.Desktop.Infrastructure.Scanning;

/// <summary>
/// 把 <c>*</c> / <c>?</c> 形式的通配路径展开成真实路径。
/// 中间段只匹配目录，末段也接受文件（WSL / Docker 的 ext4.vhdx 就是单文件目标）；
/// 只在单层目录上匹配，不做递归，最多返回 <paramref name="maxResults"/> 条。
/// </summary>
internal static class GlobPathExpander
{
    public static IReadOnlyList<string> Expand(IReadOnlyList<string> patterns, int maxResults = 400)
    {
        var results = new List<string>();

        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern) || !Path.IsPathFullyQualified(pattern))
            {
                continue;
            }

            if (!pattern.Contains('*') && !pattern.Contains('?'))
            {
                results.Add(pattern);
                continue;
            }

            ExpandPattern(pattern, results, maxResults);
            if (results.Count >= maxResults)
            {
                break;
            }
        }

        return results.Distinct(StringComparer.OrdinalIgnoreCase).Take(maxResults).ToArray();
    }

    private static void ExpandPattern(string pattern, List<string> results, int maxResults)
    {
        var root = Path.GetPathRoot(pattern);
        if (string.IsNullOrEmpty(root))
        {
            return;
        }

        var segments = pattern[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        var current = new List<string> { Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar };
        foreach (var segment in segments)
        {
            var next = new List<string>();
            foreach (var directory in current)
            {
                if (segment.Contains('*') || segment.Contains('?'))
                {
                    next.AddRange(EnumerateMatchingDirectories(directory, segment, maxResults - results.Count));
                }
                else
                {
                    next.Add(Path.Combine(directory, segment));
                }
            }

            if (next.Count == 0)
            {
                return;
            }

            current = next;
            if (results.Count + current.Count >= maxResults)
            {
                break;
            }
        }

        results.AddRange(current.Where(path => Directory.Exists(path) || File.Exists(path)));
    }

    private static IEnumerable<string> EnumerateMatchingDirectories(string directory, string pattern, int limit)
    {
        if (limit <= 0 || !Directory.Exists(directory))
        {
            return [];
        }

        try
        {
            return Directory
                .EnumerateDirectories(directory)
                .Where(candidate => FileSystemName.MatchesSimpleExpression(
                    pattern,
                    Path.GetFileName(candidate),
                    ignoreCase: true))
                .Take(limit)
                .ToArray();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }
}
