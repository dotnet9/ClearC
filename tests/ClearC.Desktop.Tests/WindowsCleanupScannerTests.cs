using ClearC.Core.Models;
using ClearC.Desktop.Infrastructure.Scanning;

namespace ClearC.Desktop.Tests;

public sealed class WindowsCleanupScannerTests
{
    [Fact]
    public void Catalog_CodexTargetScansOnlyConversationDirectories()
    {
        var target = new WindowsCleanupTargetCatalog().GetTargets().Single(item => item.Id == "codex-data");

        Assert.Equal("codex-conversations", target.CleanerKey);
        Assert.Equal(CleanupRisk.High, target.Risk);
        Assert.False(target.IsProtected);
        Assert.Equal(["sessions", "archived_sessions"], target.Paths.Select(Path.GetFileName));
    }

    [Fact]
    public async Task ScanAsync_CombinesCatalogTargetsAndRecycleBin()
    {
        var target = new CleanupTargetDefinition(
            "cache", "Cache", @"C:\Cache", [@"C:\Cache"],
            CleanupCategory.PackageCache, CleanupRisk.Low, "description", "cache");
        var scanner = new WindowsCleanupScanner(
            new FakeCatalog(target),
            new FakeSizeCalculator(new(1024, 4)),
            new FakeDiskProvider(),
            new FakeRecycleProvider());

        var result = await scanner.ScanAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(1024, result.Items[0].SizeBytes);
        Assert.Equal(2048, result.Items[1].SizeBytes);
        Assert.Equal("C:", result.Disk.DriveName);
    }

    private sealed class FakeCatalog(params CleanupTargetDefinition[] targets) : ICleanupTargetCatalog
    {
        public IReadOnlyList<CleanupTargetDefinition> GetTargets() => targets;
    }

    private sealed class FakeSizeCalculator(DirectorySize size) : IDirectorySizeCalculator
    {
        public Task<DirectorySize> CalculateAsync(IReadOnlyList<string> paths, DateTimeOffset? modifiedBefore = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(size);
    }

    private sealed class FakeDiskProvider : IDiskInfoProvider
    {
        public DiskSnapshot GetSystemDrive() => new("C:", "NTFS", 100_000, 40_000);
    }

    private sealed class FakeRecycleProvider : IRecycleBinInfoProvider
    {
        public DirectorySize GetInfo(string driveRoot) => new(2048, 8);
    }
}
