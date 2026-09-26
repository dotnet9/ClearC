using ClearC.Desktop.Infrastructure.Cleanup;
using ClearC.Desktop.Infrastructure.Scanning;
using Microsoft.Win32;

namespace ClearC.Desktop.Tests;

public sealed class TargetPathResolverTests
{
    [Fact]
    public void ResolveDefaults_FallsBackToTheDefaultPathsWithoutEnvironmentOverrides()
    {
        using var npm = new EnvironmentScope("npm_config_cache", null);
        using var nuget = new EnvironmentScope("NUGET_PACKAGES", null);
        using var gradle = new EnvironmentScope("GRADLE_USER_HOME", null);
        var resolver = CreateResolver(new FakeRegistryReader());
        var results = resolver.ResolveDefaults();

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.Equal(Path.Combine(profile, ".nuget", "packages"), results["nuget"].Path);
        Assert.Equal(Path.Combine(localAppData, "npm-cache"), results["npm"].Path);
        Assert.Equal(Path.Combine(profile, ".gradle", "caches"), results["gradle"].Path);
        Assert.All(results.Values, resolution => Assert.False(string.IsNullOrWhiteSpace(resolution.Source)));
    }

    [Fact]
    public void ResolveDefaults_ReadsTheSteamInstallPathFromTheRegistry()
    {
        var registry = new FakeRegistryReader
        {
            [RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath"] = @"D:\Steam"
        };
        var resolver = CreateResolver(registry);

        var results = resolver.ResolveDefaults();

        Assert.Equal(@"D:\Steam", results["steam"].Path);
        Assert.Equal("注册表", results["steam"].Source);
    }

    [Fact]
    public void ResolveDefaults_IgnoresAnEmptyRegistryValue()
    {
        var registry = new FakeRegistryReader
        {
            [RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath"] = "   "
        };
        var resolver = CreateResolver(registry);

        Assert.False(resolver.ResolveDefaults().ContainsKey("steam"));
    }

    [Fact]
    public void ResolveDefaults_HonoursEnvironmentOverrides()
    {
        var resolver = CreateResolver(new FakeRegistryReader());
        using var _ = new EnvironmentScope("GRADLE_USER_HOME", @"D:\gradle-home");

        var results = resolver.ResolveDefaults();

        Assert.Equal(Path.Combine(@"D:\gradle-home", "caches"), results["gradle"].Path);
        Assert.Equal("环境变量", results["gradle"].Source);
    }

    [Fact]
    public void ResolveDefaults_PrefersTheNuGetEnvironmentVariableOverTheConfigFile()
    {
        var resolver = CreateResolver(new FakeRegistryReader());
        using var _ = new EnvironmentScope("NUGET_PACKAGES", @"D:\nuget-packages");

        Assert.Equal(@"D:\nuget-packages", resolver.ResolveDefaults()["nuget"].Path);
    }

    /// <summary>
    /// 读取 <c>NuGet.Config</c> 的 <c>globalPackagesFolder</c> 并展开其中的环境变量；
    /// 写完立即还原原文件，不改变开发机的 NuGet 配置。
    /// </summary>
    [Fact]
    public void ResolveDefaults_ReadsAndExpandsTheNuGetConfigValue()
    {
        using var nuget = new EnvironmentScope("NUGET_PACKAGES", null);
        var configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NuGet",
            "NuGet.Config");
        var existed = File.Exists(configPath);
        var backup = existed ? File.ReadAllBytes(configPath) : null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            File.WriteAllText(
                configPath,
                """
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <config>
                    <add key="globalPackagesFolder" value="%TEMP%\clearc-nuget" />
                  </config>
                </configuration>
                """);

            var results = CreateResolver(new FakeRegistryReader()).ResolveDefaults();

            // %TEMP% 会展开成 8.3 短名形式，因此只断言末段与来源，并确认不再回退到默认目录。
            Assert.Equal("clearc-nuget", Path.GetFileName(results["nuget"].Path));
            Assert.True(Path.IsPathFullyQualified(results["nuget"].Path));
            Assert.Equal("NuGet.Config", results["nuget"].Source);
            Assert.NotEqual(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages"),
                results["nuget"].Path);
        }
        finally
        {
            if (backup is null)
            {
                File.Delete(configPath);
            }
            else
            {
                File.WriteAllBytes(configPath, backup);
            }
        }
    }

    [Fact]
    public async Task ResolveAsync_UsesTheCliResultWhenTheCommandSucceeds()
    {
        var probePath = Path.Combine(Path.GetTempPath(), $"clearc-probe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(probePath);
        try
        {
            var runner = new FakeProcessRunner(new Dictionary<string, ProcessRunResult>
            {
                ["npm config get cache"] = new(0, probePath + Environment.NewLine, string.Empty)
            });
            var resolver = CreateResolver(new FakeRegistryReader(), runner);

            var results = await resolver.ResolveAsync(TestContext.Current.CancellationToken);

            Assert.Equal(probePath, results["npm"].Path);
            Assert.Equal("CLI 探测", results["npm"].Source);
        }
        finally
        {
            Directory.Delete(probePath, true);
        }
    }

    [Fact]
    public async Task ResolveAsync_KeepsTheDefaultPathWhenTheCommandIsMissing()
    {
        var runner = new FakeProcessRunner(new Dictionary<string, ProcessRunResult>
        {
            ["npm config get cache"] = new(-1, string.Empty, "系统找不到指定的文件。")
        });
        var resolver = CreateResolver(new FakeRegistryReader(), runner);

        var results = await resolver.ResolveAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "npm-cache"),
            results["npm"].Path);
    }

    [Fact]
    public async Task ResolveAsync_IgnoresARelativeCliResult()
    {
        var runner = new FakeProcessRunner(new Dictionary<string, ProcessRunResult>
        {
            ["npm config get cache"] = new(0, "relative-cache", string.Empty)
        });
        var resolver = CreateResolver(new FakeRegistryReader(), runner);

        var results = await resolver.ResolveAsync(TestContext.Current.CancellationToken);

        Assert.True(Path.IsPathFullyQualified(results["npm"].Path));
    }

    [Fact]
    public async Task ResolveAsync_IgnoresACliResultPointingAtAFile()
    {
        var file = Path.Combine(Path.GetTempPath(), $"clearc-probe-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(file, "x", TestContext.Current.CancellationToken);
        try
        {
            var runner = new FakeProcessRunner(new Dictionary<string, ProcessRunResult>
            {
                ["npm config get cache"] = new(0, file, string.Empty)
            });
            var resolver = CreateResolver(new FakeRegistryReader(), runner);

            var results = await resolver.ResolveAsync(TestContext.Current.CancellationToken);

            Assert.NotEqual(file, results["npm"].Path);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task ResolveAsync_ParsesTheCondaPackagesDirectoryFromJson()
    {
        var packages = Path.Combine(Path.GetTempPath(), $"clearc-conda-{Guid.NewGuid():N}");
        Directory.CreateDirectory(packages);
        try
        {
            var json = $$"""{"pkgs_dirs": ["{{packages.Replace(@"\", @"\\")}}", "D:\\conda\\pkgs"]}""";
            var runner = new FakeProcessRunner(new Dictionary<string, ProcessRunResult>
            {
                ["conda info --json"] = new(0, json, string.Empty)
            });
            var resolver = CreateResolver(new FakeRegistryReader(), runner);

            var results = await resolver.ResolveAsync(TestContext.Current.CancellationToken);

            Assert.Equal(packages, results["conda"].Path);
            Assert.Equal("CLI 探测", results["conda"].Source);
        }
        finally
        {
            Directory.Delete(packages, true);
        }
    }

    private static TargetPathResolver CreateResolver(
        IRegistryReader registry,
        IProcessRunner? processRunner = null) =>
        new(processRunner ?? new FakeProcessRunner(new Dictionary<string, ProcessRunResult>()), registry);

    private sealed class FakeRegistryReader : IRegistryReader
    {
        private readonly Dictionary<(RegistryHive, string, string), string?> _values = [];

        public string? this[RegistryHive hive, string subKey, string valueName]
        {
            set => _values[(hive, subKey, valueName)] = value;
        }

        public string? ReadString(RegistryHive hive, string subKey, string valueName) =>
            _values.TryGetValue((hive, subKey, valueName), out var value) ? value : null;
    }

    private sealed class FakeProcessRunner(IReadOnlyDictionary<string, ProcessRunResult> results) : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken,
            TimeSpan? timeout = null)
        {
            var key = string.Join(' ', [fileName, .. arguments]);
            return Task.FromResult(results.TryGetValue(key, out var result)
                ? result
                : new ProcessRunResult(-1, string.Empty, "未检测到命令。"));
        }
    }

    /// <summary>临时设置进程级环境变量，<c>Dispose</c> 时还原（传 <c>null</c> 表示删除）。</summary>
    private sealed class EnvironmentScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previous;

        public EnvironmentScope(string name, string? value)
        {
            _name = name;
            _previous = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
    }
}
