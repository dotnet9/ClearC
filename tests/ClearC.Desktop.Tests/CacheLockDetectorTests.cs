using ClearC.Desktop.Infrastructure.Cleanup;

namespace ClearC.Desktop.Tests;

public sealed class CacheLockDetectorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ClearC.Locks.{Guid.NewGuid():N}");

    [Fact]
    public async Task FindLoadedModulesAsync_ReturnsProcessesHoldingFilesUnderTheRoot()
    {
        Directory.CreateDirectory(_root);
        var enumerator = new FakeProcessEnumerator(
        [
            (42, "dotnet", () => [Path.Combine(_root, "a.dll")]),
            (43, "notepad", () => [@"C:\Windows\System32\notepad.exe"]),
            (44, "devenv", () => [Path.Combine(_root, "nested", "b.dll")])
        ]);
        var detector = new CacheLockDetector(enumerator);

        var result = await detector.FindLoadedModulesAsync(_root, TestContext.Current.CancellationToken);

        Assert.False(result.IsPartial);
        Assert.Equal(new[] { 42, 44 }, result.Locks.Select(lockEntry => lockEntry.ProcessId));
        Assert.Contains("dotnet", result.Locks[0].ProcessName);
    }

    [Fact]
    public async Task FindLoadedModulesAsync_IgnoresProcessesThatCannotBeInspected()
    {
        Directory.CreateDirectory(_root);
        var enumerator = new FakeProcessEnumerator(
        [
            (1, "system", () => throw new InvalidOperationException("拒绝访问")),
            (2, "dotnet", () => [Path.Combine(_root, "a.dll")])
        ]);
        var detector = new CacheLockDetector(enumerator);

        var result = await detector.FindLoadedModulesAsync(_root, TestContext.Current.CancellationToken);

        Assert.False(result.IsPartial);
        Assert.Equal(2, Assert.Single(result.Locks).ProcessId);
    }

    [Fact]
    public async Task FindLoadedModulesAsync_ShortCircuitsForMissingRoots()
    {
        var detector = new CacheLockDetector(new FakeProcessEnumerator([]));

        var result = await detector.FindLoadedModulesAsync(
            Path.Combine(_root, "missing"),
            TestContext.Current.CancellationToken);

        Assert.Empty(result.Locks);
        Assert.False(result.IsPartial);
    }

    /// <summary>取消后返回已收集的结果并标记为部分结果，而不是抛异常（§3.1）。</summary>
    [Fact]
    public async Task FindLoadedModulesAsync_ReturnsPartialResultsWhenCancelled()
    {
        Directory.CreateDirectory(_root);
        using var cancellation = new CancellationTokenSource();
        var enumerator = new FakeProcessEnumerator(
        [
            (1, "first", () =>
            {
                cancellation.Cancel();
                return [Path.Combine(_root, "a.dll")];
            }),
            (2, "second", () => [Path.Combine(_root, "b.dll")])
        ]);
        var detector = new CacheLockDetector(enumerator);

        var result = await detector.FindLoadedModulesAsync(_root, cancellation.Token);

        Assert.True(result.IsPartial);
        Assert.Equal(1, Assert.Single(result.Locks).ProcessId);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private sealed class FakeProcessEnumerator(
        IReadOnlyList<(int Id, string Name, Func<IReadOnlyList<string>> Modules)> processes) : IProcessEnumerator
    {
        public IEnumerable<(int Id, string Name, Func<IReadOnlyList<string>> Modules)> Enumerate() => processes;
    }
}
