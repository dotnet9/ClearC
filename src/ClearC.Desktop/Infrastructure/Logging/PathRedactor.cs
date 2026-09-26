namespace ClearC.Desktop.Infrastructure.Logging;

/// <summary>
/// 把已知的个人目录前缀替换成环境变量占位符，保证日志与导出文本不泄露真实用户名。
/// </summary>
internal static class PathRedactor
{
    private static readonly IReadOnlyList<(string Prefix, string Placeholder)> Prefixes = BuildPrefixes();

    public static string Redact(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return message;
        }

        var result = message;
        foreach (var (prefix, placeholder) in Prefixes)
        {
            result = result.Replace(prefix, placeholder, StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    private static IReadOnlyList<(string Prefix, string Placeholder)> BuildPrefixes()
    {
        var candidates = new (string? Path, string Placeholder)[]
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "%LOCALAPPDATA%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "%APPDATA%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "%ProgramData%"),
            (Path.GetTempPath(), "%TEMP%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.Windows), "%WINDIR%")
        };

        return candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Path))
            .Select(candidate => (Prefix: Trim(candidate.Path!), candidate.Placeholder))
            .DistinctBy(candidate => candidate.Prefix, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(candidate => candidate.Prefix.Length)
            .ToArray();
    }

    private static string Trim(string path) => Path.TrimEndingDirectorySeparator(path);
}
