using ClearC.Desktop.Infrastructure.Cleanup;

namespace ClearC.Desktop.Tests;

public sealed class GuardedDirectoryCleanerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ClearC.Cleaner.Tests.{Guid.NewGuid():N}");

    [Fact]
    public async Task CleanContentsAsync_DeletesOnlyFilesOlderThanCutoffAndPreservesRoot()
    {
        Directory.CreateDirectory(Path.Combine(_root, "nested"));
        var oldFile = Path.Combine(_root, "old.bin");
        var newFile = Path.Combine(_root, "nested", "new.bin");
        await File.WriteAllBytesAsync(oldFile, new byte[32], TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(newFile, new byte[64], TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddDays(-10));

        var result = await new GuardedDirectoryCleaner().CleanContentsAsync(
            [_root], DateTimeOffset.UtcNow.AddDays(-7), TestContext.Current.CancellationToken);

        Assert.Equal(32, result.FreedBytes);
        Assert.False(File.Exists(oldFile));
        Assert.True(File.Exists(newFile));
        Assert.True(Directory.Exists(_root));
    }

    [Fact]
    public async Task CleanContentsAsync_RejectsDriveRoot()
    {
        var driveRoot = Path.GetPathRoot(_root)!;

        await Assert.ThrowsAsync<InvalidOperationException>(() => new GuardedDirectoryCleaner().CleanContentsAsync(
            [driveRoot], null, TestContext.Current.CancellationToken));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
