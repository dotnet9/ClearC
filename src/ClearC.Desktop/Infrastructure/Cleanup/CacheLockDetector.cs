using System.ComponentModel;
using System.Diagnostics;

namespace ClearC.Desktop.Infrastructure.Cleanup;

internal sealed record CacheLock(int ProcessId, string ProcessName, string ModulePath);

/// <summary>超时返回的部分结果：<see cref="IsPartial"/> 为 true 表示还有进程未能检查。</summary>
internal sealed record CacheLockScanResult(IReadOnlyList<CacheLock> Locks, bool IsPartial = false);

internal interface ICacheLockDetector
{
    Task<CacheLockScanResult> FindLoadedModulesAsync(string rootPath, CancellationToken cancellationToken = default);
}

/// <summary>把进程枚举抽出来，便于测试超时与取消。</summary>
internal interface IProcessEnumerator
{
    IEnumerable<(int Id, string Name, Func<IReadOnlyList<string>> Modules)> Enumerate();
}

internal sealed class CacheLockDetector : ICacheLockDetector
{
    private static readonly TimeSpan SoftTimeout = TimeSpan.FromSeconds(10);

    private readonly IProcessEnumerator _processEnumerator;

    public CacheLockDetector()
        : this(new SystemProcessEnumerator())
    {
    }

    internal CacheLockDetector(IProcessEnumerator processEnumerator) => _processEnumerator = processEnumerator;

    public Task<CacheLockScanResult> FindLoadedModulesAsync(
        string rootPath,
        CancellationToken cancellationToken = default) => Task.Run(
        () => FindLoadedModules(rootPath, cancellationToken),
        cancellationToken);

    private CacheLockScanResult FindLoadedModules(string rootPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            return new([]);
        }

        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath)) + Path.DirectorySeparatorChar;
        var matches = new List<CacheLock>();
        var deadline = Stopwatch.StartNew();

        foreach (var process in _processEnumerator.Enumerate())
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new(matches, IsPartial: true);
            }

            if (deadline.Elapsed > SoftTimeout)
            {
                return new(matches, IsPartial: true);
            }

            try
            {
                foreach (var path in process.Modules())
                {
                    if (path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        matches.Add(new(process.Id, process.Name, path));
                        break;
                    }
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
            {
            }
        }

        return new(matches);
    }
}

internal sealed class SystemProcessEnumerator : IProcessEnumerator
{
    public IEnumerable<(int Id, string Name, Func<IReadOnlyList<string>> Modules)> Enumerate()
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                yield return (process.Id, process.ProcessName, () => ReadModules(process));
            }
        }
    }

    private static IReadOnlyList<string> ReadModules(Process process)
    {
        var paths = new List<string>();
        foreach (ProcessModule module in process.Modules)
        {
            if (module.FileName is { } fileName)
            {
                paths.Add(fileName);
            }
        }

        return paths;
    }
}
