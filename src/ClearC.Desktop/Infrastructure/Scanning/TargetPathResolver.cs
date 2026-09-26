using System.Text.RegularExpressions;
using System.Xml.Linq;
using ClearC.Desktop.Infrastructure.Cleanup;
using Microsoft.Win32;

namespace ClearC.Desktop.Infrastructure.Scanning;

internal sealed record PathResolution(string Path, string Source);

/// <summary>
/// 探测第三方工具链的真实缓存位置。每次扫描重新解析，不做跨扫描缓存，
/// 这样用户改了 <c>.npmrc</c> / <c>GRADLE_USER_HOME</c> 后立即生效。
/// </summary>
internal interface ITargetPathResolver
{
    /// <summary>不调用外部命令的解析结果（环境变量 + 注册表 + 默认路径）。</summary>
    IReadOnlyDictionary<string, PathResolution> ResolveDefaults();

    /// <summary>在默认结果之上补充 CLI 探测（<c>npm config get cache</c> 等）。</summary>
    Task<IReadOnlyDictionary<string, PathResolution>> ResolveAsync(CancellationToken cancellationToken = default);
}

internal interface IRegistryReader
{
    string? ReadString(RegistryHive hive, string subKey, string valueName);
}

internal sealed class WindowsRegistryReader : IRegistryReader
{
    public string? ReadString(RegistryHive hive, string subKey, string valueName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using var key = baseKey.OpenSubKey(subKey);
            return key?.GetValue(valueName) as string;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}

internal sealed partial class TargetPathResolver : ITargetPathResolver
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    private readonly IProcessRunner _processRunner;
    private readonly IRegistryReader _registry;
    private readonly string _userProfile;
    private readonly string _localAppData;
    private readonly string _roamingAppData;
    private readonly string _programData;
    private readonly string _systemDrive;

    public TargetPathResolver()
        : this(new ProcessRunner(), new WindowsRegistryReader())
    {
    }

    internal TargetPathResolver(IProcessRunner processRunner, IRegistryReader registry)
    {
        _processRunner = processRunner;
        _registry = registry;
        _userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        _systemDrive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? @"C:\";
    }

    public IReadOnlyDictionary<string, PathResolution> ResolveDefaults()
    {
        var results = new Dictionary<string, PathResolution>(StringComparer.Ordinal);

        // 非 Windows 平台没有这些目录与注册表项（§8.1），直接返回空表。
        if (!OperatingSystem.IsWindows())
        {
            return results;
        }

        Set(results, "npm", FromEnvironment("npm_config_cache") ?? Local("npm-cache"));
        Set(results, "pnpm", FromEnvironment("PNPM_HOME") is { } pnpmHome
            ? pnpmHome with { Path = Path.Combine(pnpmHome.Path, "store") }
            : Local("pnpm", "store"));
        Set(results, "yarn", FromEnvironment("YARN_CACHE_FOLDER") ?? Local("Yarn", "Cache"));
        Set(results, "pip", FromEnvironment("PIP_CACHE_DIR") ?? Local("pip", "Cache"));
        Set(results, "uv", FromEnvironment("UV_CACHE_DIR") ?? Local("uv", "cache"));
        Set(results, "cargo", FromEnvironment("CARGO_HOME") is { } cargoHome
            ? cargoHome with { Path = Path.Combine(cargoHome.Path, "registry", "cache") }
            : FromProfile(".cargo", "registry", "cache"));
        Set(results, "go", FromEnvironment("GOMODCACHE") ?? FromProfile("go", "pkg", "mod"));
        Set(results, "gradle", FromEnvironment("GRADLE_USER_HOME") is { } gradleHome
            ? gradleHome with { Path = Path.Combine(gradleHome.Path, "caches") }
            : FromProfile(".gradle", "caches"));
        Set(results, "maven", FromEnvironment("MAVEN_REPO_LOCAL") ?? FromProfile(".m2", "repository"));
        Set(results, "nuget", ResolveNuGetPackages() ?? FromProfile(".nuget", "packages"));
        Set(results, "composer", Local("Composer", "cache"));
        Set(results, "scoop", FromEnvironment("SCOOP") ?? FromProfile("scoop"));
        Set(results, "vcpkg", FromEnvironment("VCPKG_ROOT") ?? new(Path.Combine(_systemDrive, "vcpkg"), "默认路径"));
        Set(results, "msys2", FromEnvironment("MSYS2_ROOT") ?? new(Path.Combine(_systemDrive, "msys64"), "默认路径"));
        Set(results, "anaconda", FromProfile("anaconda3"));
        Set(results, "conda", FirstExisting(FromEnvironment("CONDA_PKGS_DIRS")) ?? FromProfile(".conda", "pkgs"));
        Set(results, "huggingface", FromEnvironment("HF_HOME") ?? FromProfile(".cache", "huggingface"));
        Set(results, "torch", FromProfile(".cache", "torch"));
        Set(results, "vscode", Roaming("Code"));
        Set(results, "jetbrains", Local("JetBrains"));
        Set(results, "unity", Local("Unity", "cache"));
        Set(results, "unreal", Local("UnrealEngine", "Common", "DerivedDataCache"));
        Set(results, "androidSdk", FromEnvironment("ANDROID_SDK_ROOT")
            ?? FromEnvironment("ANDROID_HOME")
            ?? Local("Android", "Sdk"));
        Set(results, "steam", FromRegistry(RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath"));
        Set(results, "epic", Local("EpicGamesLauncher"));
        Set(results, "gog", FirstExisting(
            FromRegistry(RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\GOG.com\GalaxyClient", "path"),
            Local("GOG.com", "Galaxy")));
        Set(results, "battlenet", FirstExisting(
            FromRegistry(RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Blizzard Entertainment", "InstallPath"),
            new(Path.Combine(_programData, "Battle.net"), "默认路径")));
        Set(results, "cn360", Local("360Chrome", "Chrome", "User Data"));
        Set(results, "cnqq", Local("Tencent", "QQBrowser", "User Data"));
        Set(results, "cnsogou", Local("SogouExplorer", "User Data"));

        return results;
    }

    public async Task<IReadOnlyDictionary<string, PathResolution>> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, PathResolution>(ResolveDefaults(), StringComparer.Ordinal);

        await ProbeAsync(results, "npm", "npm", ["config", "get", "cache"], null, cancellationToken);
        await ProbeAsync(results, "pnpm", "pnpm", ["store", "path"], null, cancellationToken);
        await ProbeAsync(results, "yarn", "yarn", ["cache", "dir"], null, cancellationToken);
        await ProbeAsync(results, "pip", "pip", ["cache", "dir"], null, cancellationToken);
        await ProbeAsync(results, "conda", "conda", ["info", "--json"], ExtractCondaPackages, cancellationToken);

        return results;
    }

    private static PathResolution? ExtractCondaPackages(string output)
    {
        var match = CondaPackagesPattern().Match(output);
        return match.Success ? new(match.Groups[1].Value.Replace(@"\\", @"\"), "CLI 探测") : null;
    }

    private async Task ProbeAsync(
        IDictionary<string, PathResolution> results,
        string key,
        string fileName,
        IReadOnlyList<string> arguments,
        Func<string, PathResolution?>? extract,
        CancellationToken cancellationToken)
    {
        var run = await _processRunner.RunAsync(fileName, arguments, cancellationToken, ProbeTimeout);
        if (!run.Succeeded)
        {
            // 未安装该命令：保留默认/环境变量结果，相关目标不会因此消失。
            return;
        }

        PathResolution? resolution;
        if (extract is not null)
        {
            resolution = extract(run.StandardOutput);
        }
        else
        {
            var value = run.StandardOutput
                .ReplaceLineEndings("\n")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault();
            resolution = string.IsNullOrWhiteSpace(value) ? null : new(value, "CLI 探测");
        }

        if (resolution is null ||
            !Path.IsPathFullyQualified(resolution.Path) ||
            File.Exists(resolution.Path))
        {
            return;
        }

        results[key] = resolution with { Path = Path.TrimEndingDirectorySeparator(resolution.Path) };
    }

    private PathResolution? ResolveNuGetPackages()
    {
        if (FromEnvironment("NUGET_PACKAGES") is { } fromEnvironment)
        {
            return fromEnvironment;
        }

        foreach (var configPath in new[]
                 {
                     Path.Combine(_roamingAppData, "NuGet", "NuGet.Config"),
                     Path.Combine(_userProfile, "nuget.config")
                 })
        {
            var folder = ReadGlobalPackagesFolder(configPath);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                return new(Path.TrimEndingDirectorySeparator(folder), "NuGet.Config");
            }
        }

        return null;
    }

    private static string? ReadGlobalPackagesFolder(string configPath)
    {
        try
        {
            if (!File.Exists(configPath))
            {
                return null;
            }

            var document = XDocument.Load(configPath);
            var value = document
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "add", StringComparison.OrdinalIgnoreCase))
                .Where(element => string.Equals(
                    (string?)element.Attribute("key"),
                    "globalPackagesFolder",
                    StringComparison.OrdinalIgnoreCase))
                .Select(element => (string?)element.Attribute("value"))
                .FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));

            return string.IsNullOrWhiteSpace(value) ? null : Environment.ExpandEnvironmentVariables(value);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return null;
        }
    }

    private static PathResolution? FromEnvironment(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value)
            ? null
            : new(Path.TrimEndingDirectorySeparator(value), "环境变量");
    }

    private PathResolution? FromRegistry(RegistryHive hive, string subKey, string valueName)
    {
        var value = _registry.ReadString(hive, subKey, valueName);
        return string.IsNullOrWhiteSpace(value)
            ? null
            : new(Path.TrimEndingDirectorySeparator(value), "注册表");
    }

    private PathResolution Local(params string[] segments) => new(Join(_localAppData, segments), "默认路径");

    private PathResolution Roaming(params string[] segments) => new(Join(_roamingAppData, segments), "默认路径");

    private PathResolution FromProfile(params string[] segments) => new(Join(_userProfile, segments), "默认路径");

    private static string Join(string root, params string[] segments) =>
        Path.Combine([root, .. segments]);

    private static PathResolution? FirstExisting(params PathResolution?[] candidates) =>
        candidates.FirstOrDefault(candidate => candidate is not null && Directory.Exists(candidate.Path))
        ?? candidates.FirstOrDefault(candidate => candidate is not null);

    private static void Set(IDictionary<string, PathResolution> results, string key, PathResolution? value)
    {
        if (value is not null)
        {
            results[key] = value;
        }
    }

    [GeneratedRegex("\"pkgs_dirs\"\\s*:\\s*\\[\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex CondaPackagesPattern();
}
