using Avalonia;
using Avalonia.Headless;

namespace ClearC.Desktop.Tests;

public static class ClearCTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public sealed class AvaloniaHeadlessFixture : IAsyncLifetime
{
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
