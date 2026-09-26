using ClearC.Core.Models;
using ClearC.Desktop.Infrastructure.Cleanup;
using ClearC.Desktop.Infrastructure.Scanning;
using ClearC.Desktop.Infrastructure.Windows;

namespace ClearC.Desktop.Tests;

/// <summary>
/// DISM / vssadmin 的输出随系统语言变化，只按数字正则解析；解析失败必须回退并标注（§5.5、§11）。
/// </summary>
public sealed class ScanProbeTests
{
    private const string DismOutput = """
        Deployment Image Servicing and Management tool
        Version: 10.0.26200.1

        Image Version: 10.0.26200.1

        Component Store (WinSxS) information:

        Windows Explorer Reported Size of Component Store : 9.42 GB
        Actual Size of Component Store : 8.71 GB
          Shared with Windows : 6.12 GB
          Backups and Disabled Features : 2.31 GB
          Cache and Temporary Data : 284 MB
        """;

    [Fact]
    public void ParseSizes_ReadsNumbersRegardlessOfTheOutputLanguage()
    {
        var sizes = DismProbe.ParseSizes(DismOutput);

        // 版本号里的数字不会被当成大小：只有 9.42/8.71/6.12/2.31 GB 与 284 MB 命中。
        Assert.Equal(5, sizes.Count);
        Assert.Equal((long)(9.42 * 1024 * 1024 * 1024), sizes[0]);
        Assert.Equal((long)(8.71 * 1024 * 1024 * 1024), sizes[1]);
        Assert.Equal(284L * 1024 * 1024, sizes[4]);
    }

    [Fact]
    public void ParseSizes_ReturnsEmptyForUnparsableOutput()
    {
        Assert.Empty(DismProbe.ParseSizes("Error: 740, 提升权限后才能完成请求的操作。"));
    }

    [Fact]
    public async Task DismProbe_UsesTheOfficialNumbersWhenElevated()
    {
        var probe = new DismProbe(
            new FakeSizeCalculator(new(1024, 1)),
            new FakeProcessRunner(new ProcessRunResult(0, DismOutput, string.Empty)),
            new FakeElevationService(true));

        var result = await probe.ProbeAsync(CreateTarget(), TestContext.Current.CancellationToken);

        Assert.Equal((long)(8.71 * 1024 * 1024 * 1024), result.Bytes);
        Assert.Contains("DISM 官方数字", result.Note);
        Assert.Contains("可回收约", result.Note);
    }

    [Fact]
    public async Task DismProbe_FallsBackToTheDirectoryScanWhileNotElevated()
    {
        var probe = new DismProbe(
            new FakeSizeCalculator(new(4096, 7)),
            new FakeProcessRunner(new ProcessRunResult(0, DismOutput, string.Empty)),
            new FakeElevationService(false));

        var result = await probe.ProbeAsync(CreateTarget(), TestContext.Current.CancellationToken);

        Assert.Equal(4096, result.Bytes);
        Assert.Contains("需管理员", result.Note);
        Assert.Contains("偏大", result.Note);
    }

    [Fact]
    public async Task DismProbe_FallsBackWhenTheCommandFailsOrTimesOut()
    {
        var calculator = new FakeSizeCalculator(new(2048, 3));
        var failing = new DismProbe(
            calculator,
            new FakeProcessRunner(new ProcessRunResult(740, string.Empty, "错误 740")),
            new FakeElevationService(true));
        var timingOut = new DismProbe(
            calculator,
            new FakeProcessRunner(new ProcessRunResult(-1, string.Empty, string.Empty, TimedOut: true)),
            new FakeElevationService(true));

        var failed = await failing.ProbeAsync(CreateTarget(), TestContext.Current.CancellationToken);
        var timedOut = await timingOut.ProbeAsync(CreateTarget(), TestContext.Current.CancellationToken);

        Assert.Equal(2048, failed.Bytes);
        Assert.Contains("DISM 分析失败", failed.Note);
        Assert.Equal(2048, timedOut.Bytes);
        Assert.Contains("DISM 分析超时", timedOut.Note);
    }

    [Fact]
    public async Task DismProbe_FallsBackWhenTheOutputCannotBeParsed()
    {
        var probe = new DismProbe(
            new FakeSizeCalculator(new(512, 2)),
            new FakeProcessRunner(new ProcessRunResult(0, "不支持的命令", string.Empty)),
            new FakeElevationService(true));

        var result = await probe.ProbeAsync(CreateTarget(), TestContext.Current.CancellationToken);

        Assert.Equal(512, result.Bytes);
        Assert.Contains("无法解析 DISM 数字", result.Note);
    }

    [Fact]
    public async Task VssProbe_ReportsTheUsedAndMaximumShadowStorage()
    {
        const string output = """
            用于卷影副本的卷影存储关联
               卷: (C:) \\?\Volume{11111111-1111-1111-1111-111111111111}\
               卷影副本存储卷: (C:)
               已用卷影副本存储空间: 3.42 GB (13%)
               分配的最大卷影副本存储空间: 25.0 GB (10%)
            使用的卷影副本存储空间: 3.42 GB
            """;
        var probe = new VssProbe(
            new FakeProcessRunner(new ProcessRunResult(0, output, string.Empty)),
            new FakeElevationService(true));

        var result = await probe.ProbeAsync(CreateTarget(), TestContext.Current.CancellationToken);

        Assert.Equal((long)(3.42 * 1024 * 1024 * 1024), result.Bytes);
        Assert.Contains("已用", result.Note);
        Assert.Contains("上限", result.Note);
    }

    [Fact]
    public async Task VssProbe_AsksForElevationWhileNotElevated()
    {
        var probe = new VssProbe(
            new FakeProcessRunner(new ProcessRunResult(0, string.Empty, string.Empty)),
            new FakeElevationService(false));

        var result = await probe.ProbeAsync(CreateTarget(), TestContext.Current.CancellationToken);

        Assert.Equal(0, result.Bytes);
        Assert.Equal("需管理员查看卷影副本占用", result.Note);
    }

    [Fact]
    public async Task VssProbe_ReportsTheFailureToParseInsteadOfShowingZero()
    {
        var probe = new VssProbe(
            new FakeProcessRunner(new ProcessRunResult(0, "命令执行完成，但没有可显示的数据。", string.Empty)),
            new FakeElevationService(true));

        var result = await probe.ProbeAsync(CreateTarget(), TestContext.Current.CancellationToken);

        Assert.Equal(0, result.Bytes);
        Assert.Contains("无法解析", result.Note);
    }

    [Fact]
    public async Task DirectorySizeProbe_ReportsTooLongPathFiles()
    {
        var probe = new DirectorySizeProbe(new FakeSizeCalculator(new(1024, 3, null, TooLongPathFiles: 5)));

        var result = await probe.ProbeAsync(CreateTarget(), TestContext.Current.CancellationToken);

        Assert.True(result.TooLongPathFiles);
        Assert.Contains("5 个超长路径文件", result.Note);
    }

    private static CleanupTargetDefinition CreateTarget() => new(
        "winsxs",
        "组件存储 WinSxS",
        CleanupCategory.SystemFiles,
        CleanupRisk.High,
        "description",
        [@"C:\Windows\WinSxS"],
        ScanKind: ScanKind.DismAnalyze,
        Tier: ScanTier.Slow,
        RequiresElevation: true);

    private sealed class FakeSizeCalculator(DirectorySize size) : IDirectorySizeCalculator
    {
        public Task<DirectorySize> CalculateAsync(
            DirectorySizeRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(size);
    }

    private sealed class FakeProcessRunner(ProcessRunResult result) : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken,
            TimeSpan? timeout = null) => Task.FromResult(result);
    }

    private sealed class FakeElevationService(bool isElevated) : IElevationService
    {
        public bool IsElevated { get; } = isElevated;

        public bool RestartElevated() => false;
    }
}
