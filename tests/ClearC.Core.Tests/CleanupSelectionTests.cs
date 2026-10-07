using ClearC.Core.Models;
using ClearC.Core.Selection;

namespace ClearC.Core.Tests;

public sealed class CleanupSelectionTests
{
    [Fact]
    public void Constructor_SelectsOnlyLowRiskCleanableItems()
    {
        var selection = new CleanupSelection(CreateItems());

        Assert.Equal(1, selection.SelectedCount);
        Assert.True(selection.IsSelected("low"));
        Assert.False(selection.IsSelected("medium"));
        Assert.False(selection.IsSelected("protected"));
    }

    [Fact]
    public void SetAll_DoesNotSelectNonCleanableItems()
    {
        var selection = new CleanupSelection(CreateItems());

        selection.SetAll(true);

        Assert.Equal(2, selection.SelectedCount);
        Assert.Equal(300, selection.SelectedBytes);
    }

    [Fact]
    public void Constructor_DoesNotSelectEmptyCaches()
    {
        var emptyItem = new CleanupItem(
            "empty", "Empty", @"C:\Cache", CleanupCategory.PackageCache,
            CleanupRisk.Low, 0, 0, "", "cache", CleanerKind: CleanerKind.DirectoryContents);

        var selection = new CleanupSelection([emptyItem]);

        Assert.Empty(selection.SelectedIds);
    }

    /// <summary>默认勾选策略（§多盘）：其他盘符的低风险项也不默认勾选，交给用户决策。</summary>
    [Fact]
    public void Constructor_DoesNotRecommendLowRiskItemsOnNonSystemDrives()
    {
        var dTemp = new CleanupItem(
            "d-temp", "D 盘临时目录", @"D:\Temp", CleanupCategory.TemporaryFiles,
            CleanupRisk.Low, 500, 2, "", "d-temp", CleanerKind: CleanerKind.DirectoryContents);

        var selection = new CleanupSelection([dTemp]);

        Assert.Empty(selection.SelectedIds);
        Assert.False(dTemp.IsRecommended);
        Assert.False(dTemp.IsOnSystemDrive);
        Assert.Equal("D:", dTemp.DriveName);
    }

    private static CleanupItem[] CreateItems() =>
    [
        new("low", "Low", @"C:\Temp", CleanupCategory.TemporaryFiles, CleanupRisk.Low, 100, 1, "", "temp", CleanerKind: CleanerKind.DirectoryContents),
        new("medium", "Medium", @"C:\Cache", CleanupCategory.PackageCache, CleanupRisk.Medium, 200, 1, "", "cache", CleanerKind: CleanerKind.DirectoryContents),
        new("protected", "Protected", @"C:\Users\test\Documents", CleanupCategory.ApplicationData, CleanupRisk.Low, 300, 1, "", null, IsProtected: true, CleanerKind: CleanerKind.DirectoryContents)
    ];
}
