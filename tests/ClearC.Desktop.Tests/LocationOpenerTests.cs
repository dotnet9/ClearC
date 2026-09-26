using ClearC.Desktop.Infrastructure.Windows;

namespace ClearC.Desktop.Tests;

/// <summary>
/// 只覆盖失败路径：成功时会真的拉起资源管理器，不能在测试里执行。
/// </summary>
public sealed class LocationOpenerTests
{
    [Fact]
    public void TryOpen_RejectsAnEmptyPath()
    {
        Assert.False(string.IsNullOrWhiteSpace(new LocationOpener().TryOpen("   ")));
    }

    [Fact]
    public void TryOpen_RejectsARelativePath()
    {
        Assert.False(string.IsNullOrWhiteSpace(new LocationOpener().TryOpen("Cache")));
    }

    [Fact]
    public void TryOpen_ReportsAPathThatNoLongerExists()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"clearc-missing-{Guid.NewGuid():N}");

        var reason = new LocationOpener().TryOpen(missing);

        Assert.NotNull(reason);
        Assert.Contains("已不存在", reason);
    }
}
