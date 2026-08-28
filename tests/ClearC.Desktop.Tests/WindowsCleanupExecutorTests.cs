using ClearC.Core.Models;
using ClearC.Desktop.Infrastructure.Cleanup;
using ClearC.Desktop.Infrastructure.Scanning;

namespace ClearC.Desktop.Tests;

public sealed class WindowsCleanupExecutorTests
{
    [Fact]
    public async Task CleanAsync_SkipsNuGetGlobalCacheWhenLoadedModulesAreDetected()
    {
        var target = CreateTarget("nuget-global", "nuget-global");
        var process = new FakeProcessRunner();
        var executor = CreateExecutor(target, process, [new(42, "dotnet", @"C:\Cache\locked.dll")]);

        var result = await executor.CleanAsync(
            [CreateItem(target)], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(CleanupOutcome.Skipped, result.Items[0].Outcome);
        Assert.Contains("PID 42", result.Items[0].Message);
        Assert.Equal(0, process.CallCount);
    }

    [Fact]
    public async Task CleanAsync_UsesOfficialNuGetCommandForUnlockedCache()
    {
        var target = CreateTarget("nuget-http", "nuget-http");
        var process = new FakeProcessRunner();
        var executor = CreateExecutor(target, process, []);

        var result = await executor.CleanAsync(
            [CreateItem(target)], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(CleanupOutcome.Completed, result.Items[0].Outcome);
        Assert.Equal(1, process.CallCount);
        Assert.Equal(["nuget", "locals", "http-cache", "--clear"], process.Arguments);
    }

    private static WindowsCleanupExecutor CreateExecutor(
        CleanupTargetDefinition target,
        FakeProcessRunner process,
        IReadOnlyList<CacheLock> locks) => new(
            new FakeCatalog(target),
            process,
            new FakeDirectoryCleaner(),
            new FakeLockDetector(locks),
            new FakeRecycleBinCleaner());

    private static CleanupTargetDefinition CreateTarget(string id, string cleanerKey) => new(
        id, id, @"C:\Cache", [@"C:\Cache"], CleanupCategory.PackageCache,
        CleanupRisk.Low, "description", cleanerKey);

    private static CleanupItem CreateItem(CleanupTargetDefinition target) => new(
        target.Id, target.DisplayName, target.Location, target.Category, target.Risk,
        1024, 1, target.Description, target.CleanerKey);

    private sealed class FakeCatalog(params CleanupTargetDefinition[] targets) : ICleanupTargetCatalog
    {
        public IReadOnlyList<CleanupTargetDefinition> GetTargets() => targets;
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        public int CallCount { get; private set; }
        public IReadOnlyList<string> Arguments { get; private set; } = [];

        public Task<ProcessRunResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            CallCount++;
            Arguments = arguments;
            return Task.FromResult(new ProcessRunResult(0, "ok", string.Empty));
        }
    }

    private sealed class FakeDirectoryCleaner : IGuardedDirectoryCleaner
    {
        public Task<DirectoryCleanupResult> CleanContentsAsync(IReadOnlyList<string> approvedRoots, DateTimeOffset? modifiedBefore, CancellationToken cancellationToken) =>
            Task.FromResult(new DirectoryCleanupResult(0, 0, 0));
    }

    private sealed class FakeLockDetector(IReadOnlyList<CacheLock> locks) : ICacheLockDetector
    {
        public IReadOnlyList<CacheLock> FindLoadedModules(string rootPath) => locks;
    }

    private sealed class FakeRecycleBinCleaner : IRecycleBinCleaner
    {
        public bool Empty(string driveRoot, out string error)
        {
            error = string.Empty;
            return true;
        }
    }
}
