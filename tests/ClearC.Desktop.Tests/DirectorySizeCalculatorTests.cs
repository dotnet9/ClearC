using ClearC.Desktop.Infrastructure.Scanning;

namespace ClearC.Desktop.Tests;

public sealed class DirectorySizeCalculatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ClearC.Tests.{Guid.NewGuid():N}");

    [Fact]
    public async Task CalculateAsync_CountsNestedFilesAndAppliesAgeCutoff()
    {
        Directory.CreateDirectory(Path.Combine(_root, "nested"));
        var oldFile = Path.Combine(_root, "old.bin");
        var newFile = Path.Combine(_root, "nested", "new.bin");
        await File.WriteAllBytesAsync(oldFile, new byte[32], TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(newFile, new byte[64], TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddDays(-10));

        var result = await new DirectorySizeCalculator().CalculateAsync(
            new DirectorySizeRequest([_root], DateTimeOffset.UtcNow.AddDays(-7)),
            TestContext.Current.CancellationToken);

        Assert.Equal(32, result.Bytes);
        Assert.Equal(1, result.FileCount);
    }

    [Fact]
    public async Task CalculateAsync_ReportsTheNewestWriteTime()
    {
        Directory.CreateDirectory(_root);
        var file = Path.Combine(_root, "a.bin");
        await File.WriteAllBytesAsync(file, new byte[8], TestContext.Current.CancellationToken);
        var written = File.GetLastWriteTimeUtc(file);

        var result = await new DirectorySizeCalculator().CalculateAsync(
            new DirectorySizeRequest([_root]),
            TestContext.Current.CancellationToken);

        Assert.NotNull(result.LastWriteTimeUtc);
        Assert.Equal(written, result.LastWriteTimeUtc!.Value.UtcDateTime, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task CalculateAsync_OnlyCountsFilesMatchingThePatterns()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllBytesAsync(Path.Combine(_root, "thumbcache_32.db"), new byte[16], TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(_root, "iconcache_48.db"), new byte[32], TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(_root, "settings.json"), new byte[64], TestContext.Current.CancellationToken);

        var result = await new DirectorySizeCalculator().CalculateAsync(
            new DirectorySizeRequest([_root], IncludePatterns: ["thumbcache_*.db", "iconcache_*.db"]),
            TestContext.Current.CancellationToken);

        Assert.Equal(48, result.Bytes);
        Assert.Equal(2, result.FileCount);
    }

    [Fact]
    public async Task CalculateAsync_ExcludesTheGivenSubtrees()
    {
        var cbs = Path.Combine(_root, "CBS");
        Directory.CreateDirectory(cbs);
        await File.WriteAllBytesAsync(Path.Combine(_root, "keep.log"), new byte[16], TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(cbs, "CBS.log"), new byte[64], TestContext.Current.CancellationToken);

        var result = await new DirectorySizeCalculator().CalculateAsync(
            new DirectorySizeRequest([_root], ExcludedPaths: [cbs]),
            TestContext.Current.CancellationToken);

        Assert.Equal(16, result.Bytes);
        Assert.Equal(1, result.FileCount);
    }

    [Fact]
    public async Task CalculateAsync_ReturnsZeroForMissingPaths()
    {
        var result = await new DirectorySizeCalculator().CalculateAsync(
            new DirectorySizeRequest([Path.Combine(_root, "does-not-exist")]),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, result.Bytes);
        Assert.Equal(0, result.FileCount);
    }

    [Fact]
    public async Task CalculateAsync_CountsASingleFileTarget()
    {
        Directory.CreateDirectory(_root);
        var file = Path.Combine(_root, "ext4.vhdx");
        await File.WriteAllBytesAsync(file, new byte[128], TestContext.Current.CancellationToken);

        var result = await new DirectorySizeCalculator().CalculateAsync(
            new DirectorySizeRequest([file]),
            TestContext.Current.CancellationToken);

        Assert.Equal(128, result.Bytes);
        Assert.Equal(1, result.FileCount);
    }

    [Fact]
    public async Task CalculateAsync_DoesNotFollowDirectorySymlinks()
    {
        Directory.CreateDirectory(Path.Combine(_root, "real"));
        await File.WriteAllBytesAsync(Path.Combine(_root, "real", "a.bin"), new byte[16], TestContext.Current.CancellationToken);
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(_root, "link"), Path.Combine(_root, "real"));
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            // 未开启开发者模式时无法创建符号链接，跳过该断言。
            return;
        }

        var result = await new DirectorySizeCalculator().CalculateAsync(
            new DirectorySizeRequest([_root]),
            TestContext.Current.CancellationToken);

        Assert.Equal(16, result.Bytes);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
