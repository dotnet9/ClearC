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
            new DirectoryCleanupRequest([_root], DateTimeOffset.UtcNow.AddDays(-7)),
            TestContext.Current.CancellationToken);

        Assert.Equal(32, result.FreedBytes);
        Assert.Equal(1, result.DeletedFiles);
        // nested 里还有未过期的 new.bin，因此它作为非空目录保留并计入残留。
        Assert.Equal(1, result.LeftoverDirectories);
        Assert.False(File.Exists(oldFile));
        Assert.True(File.Exists(newFile));
        Assert.True(Directory.Exists(_root));
    }

    /// <summary>浅层长名目录 + 深层子目录：显式深度排序后不得留下空目录（§2.5）。</summary>
    [Fact]
    public async Task CleanContentsAsync_RemovesNestedEmptyDirectoriesDeepestFirst()
    {
        var shallow = Path.Combine(_root, "a-very-long-shallow-directory-name");
        var deep = Path.Combine(shallow, "b", "c", "d");
        Directory.CreateDirectory(deep);
        await File.WriteAllBytesAsync(Path.Combine(deep, "deep.bin"), new byte[8], TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(shallow, "shallow.bin"), new byte[16], TestContext.Current.CancellationToken);

        var result = await new GuardedDirectoryCleaner().CleanContentsAsync(
            new DirectoryCleanupRequest([_root]),
            TestContext.Current.CancellationToken);

        Assert.Equal(24, result.FreedBytes);
        Assert.Equal(2, result.DeletedFiles);
        Assert.Equal(0, result.LeftoverDirectories);
        Assert.True(Directory.Exists(_root));
        Assert.False(Directory.Exists(shallow));
    }

    [Fact]
    public async Task CleanContentsAsync_CountsDirectoriesThatStillHaveContent()
    {
        var keep = Path.Combine(_root, "keep");
        Directory.CreateDirectory(keep);
        await File.WriteAllTextAsync(Path.Combine(keep, "in-use.txt"), "in use", TestContext.Current.CancellationToken);

        // 模式不匹配任何文件 → 文件全部保留 → 目录非空 → 删除失败 → 计入残留，而不是静默吞掉。
        var result = await new GuardedDirectoryCleaner().CleanContentsAsync(
            new DirectoryCleanupRequest([_root], IncludePatterns: ["nomatch_*.db"]),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, result.DeletedFiles);
        Assert.Equal(1, result.LeftoverDirectories);
        Assert.True(Directory.Exists(keep));
    }

    [Fact]
    public async Task CleanContentsAsync_KeepsDirectoryStructureWhenDeleteEmptyDirectoriesIsOff()
    {
        var nested = Path.Combine(_root, "nested");
        Directory.CreateDirectory(nested);
        var file = Path.Combine(nested, "cache.bin");
        await File.WriteAllBytesAsync(file, new byte[16], TestContext.Current.CancellationToken);

        var result = await new GuardedDirectoryCleaner().CleanContentsAsync(
            new DirectoryCleanupRequest([_root], DeleteEmptyDirectories: false),
            TestContext.Current.CancellationToken);

        Assert.Equal(16, result.FreedBytes);
        Assert.False(File.Exists(file));
        Assert.True(Directory.Exists(nested));
    }

    [Fact]
    public async Task CleanContentsAsync_DeletesOnlyFilesMatchingThePatterns()
    {
        Directory.CreateDirectory(_root);
        var matching = Path.Combine(_root, "thumbcache_32.db");
        var other = Path.Combine(_root, "settings.json");
        await File.WriteAllBytesAsync(matching, new byte[16], TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(other, new byte[32], TestContext.Current.CancellationToken);

        var result = await new GuardedDirectoryCleaner().CleanContentsAsync(
            new DirectoryCleanupRequest([_root], IncludePatterns: ["thumbcache_*.db", "iconcache_*.db"]),
            TestContext.Current.CancellationToken);

        Assert.Equal(16, result.FreedBytes);
        Assert.False(File.Exists(matching));
        Assert.True(File.Exists(other));
    }

    [Fact]
    public async Task CleanContentsAsync_RejectsDriveRoot()
    {
        var driveRoot = Path.GetPathRoot(_root)!;

        await Assert.ThrowsAsync<InvalidOperationException>(() => new GuardedDirectoryCleaner().CleanContentsAsync(
            new DirectoryCleanupRequest([driveRoot]),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CleanContentsAsync_RejectsRelativeRoot()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => new GuardedDirectoryCleaner().CleanContentsAsync(
            new DirectoryCleanupRequest(["relative\\path"]),
            TestContext.Current.CancellationToken));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
