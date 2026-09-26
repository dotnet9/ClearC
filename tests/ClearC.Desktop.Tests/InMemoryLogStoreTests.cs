using ClearC.Desktop.Infrastructure.Logging;

namespace ClearC.Desktop.Tests;

/// <summary>
/// 日志面板的数据源只能在 UI 线程读写，因此这些用例和视图测试共用同一个 headless 会话：
/// 否则一旦 Avalonia 已初始化，写入会被投递到没跑起来的 UI 线程，断言随执行顺序偶发失败。
/// </summary>
[Collection(AvaloniaHeadlessFixture.CollectionName)]
public sealed class InMemoryLogStoreTests(AvaloniaHeadlessFixture fixture)
{
    [Fact]
    public Task Write_RedactsUserProfilePrefixesBeforeStoring() => Run(() =>
    {
        var store = new InMemoryLogStore(NullApplicationLogger.Instance);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        store.Information($@"扫描 {profile}\.nuget\packages 完成");

        var entry = Assert.Single(store.Entries);
        Assert.Equal("INFO", entry.Level);
        Assert.DoesNotContain(profile, entry.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("%USERPROFILE%", entry.Message);
    });

    [Fact]
    public Task Write_PrefersTheLongestMatchingPrefix() => Run(() =>
    {
        var store = new InMemoryLogStore(NullApplicationLogger.Instance);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        store.Information($@"清理 {localAppData}\Microsoft\Edge\User Data\Cache");

        Assert.Equal(@"清理 %LOCALAPPDATA%\Microsoft\Edge\User Data\Cache", Assert.Single(store.Entries).Message);
    });

    [Fact]
    public Task Write_KeepsOnlyTheConfiguredNumberOfEntries() => Run(() =>
    {
        var store = new InMemoryLogStore(NullApplicationLogger.Instance, maxEntries: 3);

        for (var index = 0; index < 5; index++)
        {
            store.Information($"第 {index} 行");
        }

        Assert.Equal(3, store.Entries.Count);
        Assert.Equal("第 2 行", store.Entries[0].Message);
        Assert.Equal("第 4 行", store.Entries[2].Message);
    });

    [Fact]
    public Task Clear_EmptiesThePanel() => Run(() =>
    {
        var store = new InMemoryLogStore(NullApplicationLogger.Instance);
        store.Information("a");

        store.Clear();

        Assert.Empty(store.Entries);
    });

    [Fact]
    public Task ToText_ExportsEveryVisibleLine() => Run(() =>
    {
        var store = new InMemoryLogStore(NullApplicationLogger.Instance);
        store.Information("第一行");
        store.Warning("第二行");

        var text = store.ToText();

        Assert.Contains("[INFO] 第一行", text);
        Assert.Contains("[WARN] 第二行", text);
    });

    private Task Run(Action body) =>
        fixture.Session.Dispatch(body, TestContext.Current.CancellationToken);
}
