using ClearC.Core.Formatting;
using ClearC.Core.Models;

namespace ClearC.Core.Tests;

public sealed class ScanProgressTests
{
    [Fact]
    public void PlaceholderEvent_HasNoItemAndCountsAsNotCompleted()
    {
        var progress = new ScanProgress(2, 10, "Windows 临时目录");

        Assert.Null(progress.Item);
        Assert.False(progress.IsItemCompleted);
        Assert.Equal(ScanTier.Fast, progress.Tier);
    }

    [Fact]
    public void CompletedEvent_CarriesTheFinishedItem()
    {
        var item = new CleanupItem(
            "win-temp", "Windows 临时目录", @"C:\Windows\Temp",
            CleanupCategory.TemporaryFiles, CleanupRisk.Low, 1024, 3, "", "win-temp");

        var progress = new ScanProgress(3, 10, "Windows 临时目录", ScanTier.Slow, item);

        Assert.True(progress.IsItemCompleted);
        Assert.Same(item, progress.Item);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(5, 0, 0)]
    [InlineData(0, 10, 0)]
    [InlineData(5, 10, 0.5)]
    [InlineData(10, 10, 1)]
    [InlineData(20, 10, 1)]
    public void Ratio_ClampsToTheUnitInterval(int completed, int total, double expected)
    {
        var progress = new ScanProgress(completed, total, "目标");

        Assert.Equal(expected, progress.Ratio);
    }

    [Fact]
    public void DisplayText_ShowsTheUpcomingIndexWhileTheTargetIsStillRunning()
    {
        var progress = new ScanProgress(2, 10, "浏览器缓存");

        Assert.Equal("03/10 · 浏览器缓存", progress.DisplayText);
    }

    [Fact]
    public void DisplayText_UsesTheCompletedIndexAndMarksTheSlowTier()
    {
        var item = new CleanupItem(
            "winsxs", "组件存储 WinSxS", @"C:\Windows\WinSxS",
            CleanupCategory.SystemFiles, CleanupRisk.High, 0, 0, "", null);
        var progress = new ScanProgress(3, 10, "组件存储 WinSxS", ScanTier.Slow, item);

        Assert.Equal("03/10 · 组件存储 WinSxS（慢速目标）", progress.DisplayText);
    }

    [Fact]
    public void DisplayText_DoesNotRunPastTheTotal()
    {
        var progress = new ScanProgress(10, 10, "完成");

        Assert.Equal("10/10 · 完成", progress.DisplayText);
    }

    /// <summary>幽灵行显示真实路径，让用户知道正在分析哪里（体验：慢档不再像卡住）。</summary>
    [Fact]
    public void GhostText_PrefersTheRealPathOverTheDisplayName()
    {
        var progress = new ScanProgress(2, 10, "浏览器缓存", ScanTier.Fast, null, @"C:\Users\test\AppData\Local\Microsoft\Edge\User Data");

        Assert.Equal(@"▍ 正在扫描 C:\Users\test\AppData\Local\Microsoft\Edge\User Data …", progress.GhostText);
    }

    [Fact]
    public void GhostText_FallsBackToTheDisplayNameWithoutAPath()
    {
        var progress = new ScanProgress(2, 10, "浏览器缓存");

        Assert.Equal("▍ 正在扫描 浏览器缓存 …", progress.GhostText);
    }
}

public sealed class RelativeTimeFormatterTests
{
    // 以本机当前时区的正午为基准，避免偏移换算把日期推过午夜。
    private static readonly DateTimeOffset Now = new(DateTimeOffset.Now.Date.AddHours(12), DateTimeOffset.Now.Offset);

    [Fact]
    public void Format_ReturnsDashWithoutATimestamp()
    {
        Assert.Equal("—", RelativeTimeFormatter.Format(null, Now));
    }

    [Fact]
    public void Format_UsesTimeOfDayForToday()
    {
        var value = Now.AddHours(-4);

        Assert.Equal($"今天 {value:HH:mm}", RelativeTimeFormatter.Format(value, Now));
    }

    [Fact]
    public void Format_UsesYesterdayForThePreviousDay()
    {
        var value = Now.AddDays(-1).AddHours(9);

        Assert.Equal($"昨天 {value:HH:mm}", RelativeTimeFormatter.Format(value, Now));
    }

    [Fact]
    public void Format_UsesDayCountBeyondYesterday()
    {
        Assert.Equal("7 天前", RelativeTimeFormatter.Format(Now.AddDays(-7), Now));
    }
}
