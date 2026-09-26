using Avalonia;
using Avalonia.Headless;

namespace ClearC.Desktop.Tests;

public static class ClearCTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

/// <summary>
/// 整个程序集共用一个 headless 会话：视图模型与视图测试都需要一个会跑消息循环的 UI 线程。
/// </summary>
public sealed class AvaloniaHeadlessFixture : IAsyncLifetime
{
    public const string CollectionName = "avalonia-headless";

    public AvaloniaHeadlessFixture()
    {
        Session = HeadlessUnitTestSession.StartNew(
            typeof(ClearCTestAppBuilder),
            AvaloniaTestIsolationLevel.PerAssembly);
    }

    public HeadlessUnitTestSession Session { get; }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await Task.Run(() => Session.DisposeAsync().AsTask()).ConfigureAwait(false);
    }
}

[CollectionDefinition(AvaloniaHeadlessFixture.CollectionName)]
public sealed class AvaloniaHeadlessCollection : ICollectionFixture<AvaloniaHeadlessFixture>;
