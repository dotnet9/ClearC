using System.Diagnostics;

namespace ClearC.Desktop.Infrastructure.Cleanup;

internal sealed record CacheLock(int ProcessId, string ProcessName, string ModulePath);

internal interface ICacheLockDetector
{
    IReadOnlyList<CacheLock> FindLoadedModules(string rootPath);
}

internal sealed class CacheLockDetector : ICacheLockDetector
{
    public IReadOnlyList<CacheLock> FindLoadedModules(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            return [];
        }

        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath)) + Path.DirectorySeparatorChar;
        var matches = new List<CacheLock>();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    foreach (ProcessModule module in process.Modules)
                    {
                        var path = module.FileName;
                        if (path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                        {
                            matches.Add(new(process.Id, process.ProcessName, path));
                            break;
                        }
                    }
                }
                catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                }
            }
        }

        return matches;
    }
}
