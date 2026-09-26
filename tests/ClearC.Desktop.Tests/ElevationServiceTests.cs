using ClearC.Desktop.Infrastructure.Cleanup;
using ClearC.Desktop.Infrastructure.Scanning;
using ClearC.Desktop.Infrastructure.Windows;

namespace ClearC.Desktop.Tests;

public sealed class ElevationServiceTests
{
    [Fact]
    public void RestartElevated_DoesNothingWhenAlreadyElevated()
    {
        var starter = new FakeProcessStarter();
        var service = new ElevationService(new FakeElevationProbe(true), starter);

        Assert.True(service.IsElevated);
        Assert.False(service.RestartElevated());
        Assert.Equal(0, starter.CallCount);
    }

    [Fact]
    public void RestartElevated_StartsTheCurrentExecutable()
    {
        var starter = new FakeProcessStarter();
        var service = new ElevationService(new FakeElevationProbe(false), starter);

        Assert.True(service.RestartElevated());

        Assert.Equal(1, starter.CallCount);
        Assert.Equal(Environment.ProcessPath, starter.FileName);
    }

    /// <summary>用户拒绝 UAC：返回 false，不重试、不抛异常（§11）。</summary>
    [Fact]
    public void RestartElevated_ReturnsFalseWhenTheUserDeclinesTheUacPrompt()
    {
        var service = new ElevationService(new FakeElevationProbe(false), new FakeProcessStarter(succeeds: false));

        Assert.False(service.RestartElevated());
        Assert.False(service.IsElevated);
    }

    private sealed class FakeElevationProbe(bool isElevated) : IElevationProbe
    {
        public bool IsElevated { get; } = isElevated;
    }

    private sealed class FakeProcessStarter(bool succeeds = true) : IProcessStarter
    {
        public int CallCount { get; private set; }
        public string? FileName { get; private set; }

        public bool StartElevated(string fileName)
        {
            CallCount++;
            FileName = fileName;
            return succeeds;
        }
    }
}

public sealed class PlatformGuardTests
{
    [Fact]
    public async Task RecycleBinCleaner_RefusesToRunOffWindows()
    {
        var cleaner = new RecycleBinCleaner(new FakePlatform(false));

        var result = await cleaner.EmptyAsync(@"C:\", TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains("仅支持 Windows", result.Error);
    }

    [Fact]
    public async Task RecycleBinInfoProvider_ReturnsZeroOffWindows()
    {
        var provider = new WindowsRecycleBinInfoProvider(new FakePlatform(false));

        var size = await provider.GetInfoAsync(@"C:\", TestContext.Current.CancellationToken);

        Assert.Equal(0, size.Bytes);
        Assert.Equal(0, size.FileCount);
    }

    [Fact]
    public void SystemPlatform_TracksTheHostOperatingSystem()
    {
        Assert.Equal(OperatingSystem.IsWindows(), SystemPlatform.Instance.IsWindows);
    }

    private sealed class FakePlatform(bool isWindows) : IPlatform
    {
        public bool IsWindows { get; } = isWindows;
    }
}
