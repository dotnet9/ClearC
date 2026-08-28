using ClearC.Core.Models;

namespace ClearC.Desktop.Infrastructure.Scanning;

internal interface ICleanupTargetCatalog
{
    IReadOnlyList<CleanupTargetDefinition> GetTargets();
}

internal sealed class WindowsCleanupTargetCatalog : ICleanupTargetCatalog
{
    public IReadOnlyList<CleanupTargetDefinition> GetTargets()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var temp = Path.GetTempPath();
        var nugetPackages = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (string.IsNullOrWhiteSpace(nugetPackages))
        {
            nugetPackages = Path.Combine(userProfile, ".nuget", "packages");
        }

        var browserPaths = FindBrowserCacheDirectories(localAppData);
        var oldFileCutoff = TimeSpan.FromDays(7);

        return
        [
            new(
                "nuget-global", "NuGet 全局包缓存", nugetPackages, [nugetPackages],
                CleanupCategory.PackageCache, CleanupRisk.Medium,
                "已还原的 NuGet 包副本；清理后项目下次生成会重新下载依赖。",
                "nuget-global"),
            new(
                "nuget-http", "NuGet HTTP 缓存", Path.Combine(localAppData, "NuGet", "v3-cache"),
                [Path.Combine(localAppData, "NuGet", "v3-cache")],
                CleanupCategory.PackageCache, CleanupRisk.Low,
                "NuGet 下载缓存，可通过 dotnet 官方命令安全重建。", "nuget-http"),
            new(
                "nuget-temp", "NuGet 临时缓存", Path.Combine(temp, "NuGetScratch"),
                [Path.Combine(temp, "NuGetScratch")],
                CleanupCategory.PackageCache, CleanupRisk.Low,
                "NuGet 操作产生的临时内容，可通过 dotnet 官方命令清理。", "nuget-temp"),
            new(
                "nuget-plugins", "NuGet 插件缓存", Path.Combine(localAppData, "NuGet", "plugins-cache"),
                [Path.Combine(localAppData, "NuGet", "plugins-cache")],
                CleanupCategory.PackageCache, CleanupRisk.Low,
                "NuGet 凭据与插件进程缓存，可通过 dotnet 官方命令重建。", "nuget-plugins"),
            new(
                "user-temp", "过期临时文件", temp, [temp],
                CleanupCategory.TemporaryFiles, CleanupRisk.Low,
                "超过 7 天未修改的用户临时文件；正在使用或无权限的文件会跳过。",
                "user-temp", oldFileCutoff),
            new(
                "npm-cache", "npm 缓存", Path.Combine(localAppData, "npm-cache"),
                [Path.Combine(localAppData, "npm-cache")],
                CleanupCategory.PackageCache, CleanupRisk.Low,
                "npm 下载缓存；清理后安装依赖时会重新下载。", "npm-cache"),
            new(
                "browser-cache", "浏览器缓存", "Edge / Chrome 网页缓存", browserPaths,
                CleanupCategory.BrowserCache, CleanupRisk.Low,
                "Edge 与 Chrome 的网页、代码和 GPU 缓存，不包含历史记录、登录信息或收藏夹。",
                "browser-cache"),
            new(
                "codex-data", "Codex 工作数据", Path.Combine(userProfile, ".codex"),
                [Path.Combine(userProfile, ".codex")],
                CleanupCategory.ApplicationData, CleanupRisk.High,
                "包含会话、技能和工作数据，仅展示占用，ClearC 永不清理。",
                null, IsProtected: true),
            new(
                "windows-old", "旧版系统 Windows.old", Path.Combine(Path.GetPathRoot(windows) ?? @"C:\", "Windows.old"),
                [Path.Combine(Path.GetPathRoot(windows) ?? @"C:\", "Windows.old")],
                CleanupCategory.SystemFiles, CleanupRisk.High,
                "系统升级备份，删除后无法回滚；应使用 Windows 设置管理。",
                null, RequiresElevation: true, IsProtected: true),
            new(
                "hiberfil", "休眠文件 hiberfil.sys", Path.Combine(Path.GetPathRoot(windows) ?? @"C:\", "hiberfil.sys"),
                [Path.Combine(Path.GetPathRoot(windows) ?? @"C:\", "hiberfil.sys")],
                CleanupCategory.SystemFiles, CleanupRisk.Medium,
                "系统休眠镜像，仅展示占用；需要通过 powercfg 管理。",
                null, RequiresElevation: true, IsProtected: true),
            new(
                "pagefile", "页面文件 pagefile.sys", Path.Combine(Path.GetPathRoot(windows) ?? @"C:\", "pagefile.sys"),
                [Path.Combine(Path.GetPathRoot(windows) ?? @"C:\", "pagefile.sys")],
                CleanupCategory.SystemFiles, CleanupRisk.Medium,
                "虚拟内存交换文件，仅展示占用，不可直接清理。",
                null, RequiresElevation: true, IsProtected: true),
            new(
                "memory-dump", "系统内存转储", Path.Combine(windows, "MEMORY.DMP"),
                [Path.Combine(windows, "MEMORY.DMP")],
                CleanupCategory.SystemFiles, CleanupRisk.Medium,
                "蓝屏诊断转储，仅展示占用；确认无需排障后请使用 Windows 设置清理。",
                null, RequiresElevation: true, IsProtected: true)
        ];
    }

    private static string[] FindBrowserCacheDirectories(string localAppData)
    {
        var roots = new[]
        {
            Path.Combine(localAppData, "Microsoft", "Edge", "User Data"),
            Path.Combine(localAppData, "Google", "Chrome", "User Data")
        };
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots.Where(Directory.Exists))
        {
            AddIfExists(paths, Path.Combine(root, "ShaderCache"));
            AddIfExists(paths, Path.Combine(root, "GrShaderCache"));

            try
            {
                foreach (var profile in Directory.EnumerateDirectories(root)
                             .Where(path => Path.GetFileName(path) == "Default" || Path.GetFileName(path).StartsWith("Profile ", StringComparison.OrdinalIgnoreCase)))
                {
                    AddIfExists(paths, Path.Combine(profile, "Cache"));
                    AddIfExists(paths, Path.Combine(profile, "Code Cache"));
                    AddIfExists(paths, Path.Combine(profile, "GPUCache"));
                    AddIfExists(paths, Path.Combine(profile, "Service Worker", "CacheStorage"));
                }
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
            }
        }

        return paths.ToArray();
    }

    private static void AddIfExists(ISet<string> paths, string path)
    {
        if (Directory.Exists(path))
        {
            paths.Add(path);
        }
    }
}
