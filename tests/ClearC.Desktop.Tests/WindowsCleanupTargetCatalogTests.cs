using ClearC.Core.Models;
using ClearC.Desktop.Infrastructure.Scanning;

namespace ClearC.Desktop.Tests;

/// <summary>
/// 目标数量增加后必须有断言（§9）：id 唯一、路径安全、仅分析项无清理器、
/// 白名单非空、去重后无同一物理目录被两个目标认领。
/// </summary>
public sealed class WindowsCleanupTargetCatalogTests
{
    private static readonly IReadOnlyList<CleanupTargetDefinition> Targets =
        new WindowsCleanupTargetCatalog().GetTargets();

    [Fact]
    public void GetTargets_ReturnsTheExpectedNumberOfTargets()
    {
        Assert.InRange(Targets.Count, 80, 140);
    }

    [Fact]
    public void GetTargets_UsesUniqueIds()
    {
        var duplicates = Targets
            .GroupBy(target => target.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void GetTargets_UsesUniqueDisplayNames()
    {
        var duplicates = Targets
            .GroupBy(target => target.DisplayName, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void GetTargets_UsesFullyQualifiedPathsWithoutParentSegments()
    {
        foreach (var target in Targets)
        {
            foreach (var path in target.Paths)
            {
                Assert.True(Path.IsPathFullyQualified(path), $"{target.Id} 的路径不是绝对路径：{path}");
                Assert.DoesNotContain("..", path, StringComparison.Ordinal);
                Assert.NotEqual(Path.TrimEndingDirectorySeparator(Path.GetPathRoot(path)!), Path.TrimEndingDirectorySeparator(path));
            }
        }
    }

    [Fact]
    public void GetTargets_UsesFullyQualifiedAllowedRoots()
    {
        foreach (var target in Targets)
        {
            foreach (var root in target.EffectiveAllowedRoots)
            {
                Assert.True(Path.IsPathFullyQualified(root), $"{target.Id} 的白名单不是绝对路径：{root}");
                Assert.DoesNotContain("..", root, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// 静态定义会保留展开不出来的候选通配串（如 WSL 的 ext4.vhdx），
    /// 但清理白名单必须是真实父目录，否则 <c>IsAllowed</c> 的前缀匹配永远不成立。
    /// </summary>
    [Fact]
    public void GetTargets_KeepsGlobPatternsOutOfTheCleanWhitelist()
    {
        foreach (var target in Targets.Where(target => target.CleanerKind != CleanerKind.None))
        {
            foreach (var root in target.EffectiveAllowedRoots)
            {
                Assert.DoesNotContain('*', root);
                Assert.DoesNotContain('?', root);
            }
        }
    }

    [Fact]
    public void GetTargets_GivesEveryCleanerAnAllowedRoot()
    {
        foreach (var target in Targets.Where(target => target.CleanerKind != CleanerKind.None))
        {
            Assert.NotEmpty(target.EffectiveAllowedRoots);
        }
    }

    [Fact]
    public void GetTargets_KeepsAnalyzeOnlyTargetsWithoutACleaner()
    {
        foreach (var target in Targets.Where(target => target.CleanerKind == CleanerKind.None))
        {
            Assert.Null(target.Command);
        }
    }

    [Fact]
    public void GetTargets_RequiresOnlyAnalyzeTargetsToBeUncleanable()
    {
        foreach (var target in Targets)
        {
            var item = target.ToItem(0, 0);
            Assert.Equal(target.CleanerKind != CleanerKind.None && !target.IsProtected, item.CanClean);
            Assert.Equal(target.CleanerKind == CleanerKind.None, item.IsAnalyzeOnly);
        }
    }

    [Fact]
    public void GetTargets_ConfiguresCommandsOnlyForTheCommandCleaner()
    {
        foreach (var target in Targets)
        {
            if (target.CleanerKind == CleanerKind.Command)
            {
                Assert.NotNull(target.Command);
                Assert.NotEmpty(target.Command.Arguments);
            }
            else
            {
                Assert.Null(target.Command);
            }
        }
    }

    [Fact]
    public void GetTargets_ConfiguresPatternsOnlyForTheFilePatternCleaner()
    {
        foreach (var target in Targets.Where(target => target.CleanerKind == CleanerKind.FilePattern))
        {
            Assert.NotEmpty(target.IncludePatterns!);
        }
    }

    [Fact]
    public void GetTargets_AssignsAnAbsoluteWindowsPathOrACommandLocation()
    {
        foreach (var target in Targets)
        {
            Assert.False(string.IsNullOrWhiteSpace(target.Location));
            Assert.False(string.IsNullOrWhiteSpace(target.Description));
            Assert.StartsWith("i-", target.Icon, StringComparison.Ordinal);
            Assert.Matches("^#[0-9a-f]{6}$", target.Accent);
        }
    }

    /// <summary>WinSxS / 卷影副本只解析官方数字，绝不提供清理入口（§5.5）。</summary>
    [Theory]
    [InlineData("winsxs")]
    [InlineData("vss-shadow")]
    public void GetTargets_KeepsAnalyzeOnlySystemStoresUncleanable(string id)
    {
        var target = Targets.Single(candidate => candidate.Id == id);

        Assert.Equal(CleanerKind.None, target.CleanerKind);
        Assert.Equal(ScanTier.Slow, target.Tier);
        Assert.True(target.RequiresElevation);
        Assert.False(target.ToItem(1024, 0).CanClean);
    }

    [Fact]
    public void GetTargets_KeepsProtectedSystemFilesReadOnly()
    {
        foreach (var id in new[] { "windows-old", "hiberfil", "pagefile", "memory-dump" })
        {
            var target = Targets.Single(candidate => candidate.Id == id);

            Assert.True(target.IsProtected, $"{id} 应该标记为受保护。");
            Assert.False(target.ToItem(1024, 1).CanClean);
        }
    }

    [Fact]
    public void GetTargets_GivesEveryScanKindAUsableProbe()
    {
        var kinds = Targets.Select(target => target.ScanKind).Distinct().ToArray();

        Assert.Equal(Enum.GetValues<ScanKind>().Length, kinds.Length);
    }

    [Fact]
    public void GetTargets_AssignsSlowerTimeoutToDismAndVssTargets()
    {
        Assert.Equal(TimeSpan.FromSeconds(180), Targets.Single(t => t.Id == "winsxs").EffectiveScanTimeout);
        Assert.Equal(TimeSpan.FromSeconds(180), Targets.Single(t => t.Id == "vss-shadow").EffectiveScanTimeout);
        Assert.Equal(TimeSpan.FromSeconds(60), Targets.Single(t => t.Id == "user-temp").EffectiveScanTimeout);
    }

    [Fact]
    public void ToItem_CarriesPathsIconAndAccentToTheRow()
    {
        var target = Targets.Single(candidate => candidate.Id == "user-temp");

        var item = target.ToItem(2048, 3, DateTimeOffset.UnixEpoch, "note");

        Assert.Equal(target.Paths, item.CleanRoots);
        Assert.Equal(target.Icon, item.Icon);
        Assert.Equal(target.Accent, item.Accent);
        Assert.Equal("note", item.ScanNote);
        Assert.Equal(DateTimeOffset.UnixEpoch, item.LastWriteTimeUtc);
    }

    /// <summary>回收站是 scanner 生成的动态项，不能和 catalog 里的静态项重复计时。</summary>
    [Fact]
    public void GetTargets_ContainsTheRecycleBinTarget()
    {
        var target = Targets.Single(candidate => candidate.Id == "recycle-bin");

        Assert.Equal(CleanerKind.RecycleBin, target.CleanerKind);
        Assert.Equal("i-trash", target.Icon);
    }
}
