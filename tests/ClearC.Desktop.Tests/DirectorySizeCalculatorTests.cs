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
            [_root], DateTimeOffset.UtcNow.AddDays(-7), TestContext.Current.CancellationToken);

        Assert.Equal(32, result.Bytes);
        Assert.Equal(1, result.FileCount);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
