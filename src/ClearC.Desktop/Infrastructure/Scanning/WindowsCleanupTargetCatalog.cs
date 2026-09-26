using ClearC.Core.Models;

namespace ClearC.Desktop.Infrastructure.Scanning;

internal interface ICleanupTargetCatalog
{
    /// <summary>静态定义（含候选路径），用于执行白名单与 id → 目标映射。</summary>
    IReadOnlyList<CleanupTargetDefinition> GetTargets();

    /// <summary>
    /// 扫描用：重新解析探测路径、展开通配目录，并剔除磁盘上不存在的目标，
    /// 避免列表里出现大量 0 字节假条目。
    /// </summary>
    Task<IReadOnlyList<CleanupTargetDefinition>> ResolveTargetsAsync(CancellationToken cancellationToken = default);
}

internal sealed class WindowsCleanupTargetCatalog : ICleanupTargetCatalog
{
    private readonly ITargetPathResolver _pathResolver;

    public WindowsCleanupTargetCatalog()
        : this(new TargetPathResolver())
    {
    }

    internal WindowsCleanupTargetCatalog(ITargetPathResolver pathResolver) => _pathResolver = pathResolver;

    /// <summary>非 Windows 平台没有这些系统目录，返回空表（§8.1），避免出现 <c>C:\</c> 假设。</summary>
    public IReadOnlyList<CleanupTargetDefinition> GetTargets() =>
        OperatingSystem.IsWindows()
            ? Build(_pathResolver.ResolveDefaults(), discoveredOnly: false)
            : [];

    public async Task<IReadOnlyList<CleanupTargetDefinition>> ResolveTargetsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        var toolchain = await _pathResolver.ResolveAsync(cancellationToken);
        return Build(toolchain, discoveredOnly: true);
    }

    private static IReadOnlyList<CleanupTargetDefinition> Build(
        IReadOnlyDictionary<string, PathResolution> toolchain,
        bool discoveredOnly)
    {
        var environment = EnvironmentPaths.Create();
        var rows = BuildRows(environment, toolchain);
        var claimedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targets = new List<CleanupTargetDefinition>(rows.Count);

        foreach (var row in rows)
        {
            var candidates = row.Paths(toolchain)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var paths = discoveredOnly
                ? GlobPathExpander.Expand(candidates).Where(PathExists).ToArray()
                : DeclaredPaths(candidates);

            // 同一物理目录只归属一个目标（先注册者优先），避免重复统计与重复清理。
            paths = paths.Where(path => claimedPaths.Add(path)).ToArray();

            // 探测不到路径 = 该目标不出现；VSS 这类不依赖目录的目标除外。
            var pathLessProbe = row.ScanKind == ScanKind.VssQuery;
            if (discoveredOnly && paths.Count == 0 && !pathLessProbe)
            {
                continue;
            }

            var allowedRoots = row.AllowedRoots?.Invoke(toolchain).ToArray() ?? paths;
            if (allowedRoots.Count == 0 && !pathLessProbe)
            {
                continue;
            }

            targets.Add(new CleanupTargetDefinition(
                row.Id,
                row.Name,
                row.Category,
                row.Risk,
                row.Description,
                paths,
                row.CleanerKind,
                allowedRoots,
                row.IncludePatterns,
                row.ExcludePaths,
                row.Command,
                row.ScanKind,
                row.Tier,
                row.MinimumAge,
                row.ScanTimeout,
                row.RequiresElevation,
                row.IsProtected,
                row.CheckLoadedModules,
                row.Icon,
                row.Accent ?? DefaultAccent(row.Category),
                row.LocationOverride,
                row.PathSource?.Invoke(toolchain),
                row.ScanNote,
                row.ScanLabel));
        }

        return targets;
    }

    private static bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>
    /// 静态定义保留候选路径：通配串展开不出结果时（例如当前机器没有 WSL 磁盘镜像、
    /// 没有匹配的 Store 应用目录）保留候选通配串本身，避免整条目标从定义里消失。
    /// 扫描走 <see cref="ResolveTargetsAsync"/>，那里只保留真实存在的路径。
    /// </summary>
    private static IReadOnlyList<string> DeclaredPaths(IReadOnlyList<string> candidates)
    {
        var expanded = GlobPathExpander.Expand(candidates);
        return expanded.Count > 0 ? expanded : candidates;
    }

    private static string DefaultAccent(CleanupCategory category) => category switch
    {
        CleanupCategory.TemporaryFiles => "#2f6bff",
        CleanupCategory.SystemUpdate => "#2563eb",
        CleanupCategory.SystemLogs => "#475569",
        CleanupCategory.GraphicsAndGameCache => "#db2777",
        CleanupCategory.PackageCache => "#0d9488",
        CleanupCategory.BrowserCache => "#6366f1",
        CleanupCategory.ApplicationData => "#10b981",
        CleanupCategory.RecycleBin => "#64748b",
        _ => "#7c5cff"
    };

    private sealed record Row(
        string Id,
        string Name,
        CleanupCategory Category,
        CleanupRisk Risk,
        string Description,
        string Icon,
        Func<IReadOnlyDictionary<string, PathResolution>, IReadOnlyList<string>> Paths,
        CleanerKind CleanerKind = CleanerKind.None,
        Func<IReadOnlyDictionary<string, PathResolution>, IReadOnlyList<string>>? AllowedRoots = null,
        IReadOnlyList<string>? IncludePatterns = null,
        IReadOnlyList<string>? ExcludePaths = null,
        CleanerCommand? Command = null,
        ScanKind ScanKind = ScanKind.Directory,
        ScanTier Tier = ScanTier.Fast,
        TimeSpan? MinimumAge = null,
        TimeSpan? ScanTimeout = null,
        bool RequiresElevation = false,
        bool IsProtected = false,
        bool CheckLoadedModules = false,
        string? Accent = null,
        string? LocationOverride = null,
        Func<IReadOnlyDictionary<string, PathResolution>, string?>? PathSource = null,
        string? ScanNote = null,
        string? ScanLabel = null);

    private sealed record EnvironmentPaths(
        string SystemDrive,
        string Windows,
        string ProgramData,
        string ProgramFiles,
        string UserProfile,
        string LocalAppData,
        string RoamingAppData,
        string Temp)
    {
        public static EnvironmentPaths Create()
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            return new(
                Path.GetPathRoot(windows) ?? @"C:\",
                windows,
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Path.GetTempPath());
        }

        public string Under(string root, params string[] segments) => Path.Combine([root, .. segments]);

        public string Windows32(string relative) => Path.Combine(Windows, relative);

        public string Package(string packagePattern, params string[] segments) =>
            Path.Combine([Under(LocalAppData, "Packages"), packagePattern, .. segments]);
    }

    private static IReadOnlyList<Row> BuildRows(
        EnvironmentPaths environment,
        IReadOnlyDictionary<string, PathResolution> toolchain)
    {
        var drive = environment.SystemDrive;
        var local = environment.LocalAppData;
        var roaming = environment.RoamingAppData;
        var programData = environment.ProgramData;
        var windows = environment.Windows;
        var profile = environment.UserProfile;
        var temp = environment.Temp;

        var oldFileCutoff = TimeSpan.FromDays(7);
        var largeTreeTimeout = TimeSpan.FromSeconds(120);

        var browserPaths = FindBrowserCacheDirectories(local);
        var firefoxPaths = FindFirefoxCacheDirectories(local);
        var webViewPaths = FindWebView2CacheDirectories(local);
        var chromiumFamilyPaths = FindChromiumFamilyCacheDirectories(local);
        var cnBrowserPaths = FindChineseBrowserCacheDirectories(toolchain);

        var nugetPackages = Tool(toolchain, "nuget") ?? environment.Under(profile, ".nuget", "packages");
        var npmCache = Tool(toolchain, "npm") ?? environment.Under(local, "npm-cache");

        return
        [
            // ── G1 Windows 系统缓存 ──────────────────────────────────────────────
            new("win-temp", "Windows 临时目录", CleanupCategory.TemporaryFiles, CleanupRisk.Low,
                "系统级临时文件；正在被安装程序或 Defender 使用的文件会跳过。",
                "i-file", Fixed(environment.Windows32("Temp")),
                CleanerKind.DirectoryContents, MinimumAge: oldFileCutoff, RequiresElevation: true),
            new("wu-download", "Windows 更新下载缓存", CleanupCategory.SystemUpdate, CleanupRisk.Low,
                "已安装更新的下载残留，可安全删除；正在下载的更新会跳过。",
                "i-refresh", Fixed(environment.Windows32(@"SoftwareDistribution\Download")),
                CleanerKind.DirectoryContents, RequiresElevation: true),
            new("delivery-optimization", "传递优化缓存", CleanupCategory.SystemUpdate, CleanupRisk.Low,
                "Windows 更新与 Store 的 P2P 分发包缓存，删除后会自动重建。",
                "i-download", Fixed(environment.Under(programData, "Microsoft", "Windows", "DeliveryOptimization", "Cache")),
                CleanerKind.DirectoryContents, RequiresElevation: true),
            new("prefetch", "预取文件 Prefetch", CleanupCategory.SystemUpdate, CleanupRisk.Medium,
                "程序启动预读数据；删除后首批启动稍慢，之后自动重建。",
                "i-bolt", Fixed(environment.Windows32("Prefetch")),
                CleanerKind.DirectoryContents, RequiresElevation: true),
            new("thumbnail-icon-cache", "缩略图与图标缓存", CleanupCategory.GraphicsAndGameCache, CleanupRisk.Low,
                "资源管理器的缩略图与图标缓存，删除后打开文件夹时自动重建。",
                "i-image", Fixed(environment.Under(local, "Microsoft", "Windows", "Explorer")),
                CleanerKind.FilePattern,
                IncludePatterns: ["thumbcache_*.db", "iconcache_*.db"],
                ScanNote: "只清理 thumbcache / iconcache 数据库文件"),

            // ── G2 Windows 日志与转储 ────────────────────────────────────────────
            new("wer-archive", "Windows 错误报告", CleanupCategory.SystemLogs, CleanupRisk.Low,
                "程序崩溃诊断报告；被系统占用的报告会跳过。",
                "i-alert", Fixed(environment.Under(programData, "Microsoft", "Windows", "WER")),
                CleanerKind.DirectoryContents, RequiresElevation: true),
            new("wer-user", "用户错误报告", CleanupCategory.SystemLogs, CleanupRisk.Low,
                "当前用户的崩溃报告队列与归档。",
                "i-alert", Fixed(environment.Under(local, "Microsoft", "Windows", "WER")),
                CleanerKind.DirectoryContents),
            new("crash-dumps", "应用崩溃转储", CleanupCategory.SystemLogs, CleanupRisk.Low,
                "应用崩溃产生的转储文件；排查完故障后可删除。",
                "i-chip", Fixed(environment.Under(local, "CrashDumps")),
                CleanerKind.DirectoryContents),
            new("minidump", "小型内存转储", CleanupCategory.SystemLogs, CleanupRisk.Low,
                "蓝屏诊断用的小型转储文件，确认无需排障后可删除。",
                "i-chip", Fixed(environment.Windows32("Minidump")),
                CleanerKind.DirectoryContents, RequiresElevation: true),
            new("live-kernel-reports", "内核实时报告", CleanupCategory.SystemLogs, CleanupRisk.Low,
                "LiveKernelEvent 报告，用于硬件与驱动排障。",
                "i-doc", Fixed(environment.Windows32("LiveKernelReports")),
                CleanerKind.DirectoryContents, RequiresElevation: true),
            new("windows-panther", "安装日志 Panther", CleanupCategory.SystemLogs, CleanupRisk.Low,
                "Windows 安装与升级过程日志。",
                "i-doc", Fixed(environment.Windows32("Panther")),
                CleanerKind.DirectoryContents, RequiresElevation: true),
            new("windows-logs", "Windows 日志目录", CleanupCategory.SystemLogs, CleanupRisk.Low,
                "系统组件日志；CBS 服务日志单独列出，不在此项内。",
                "i-doc", Fixed(environment.Windows32("Logs")),
                CleanerKind.DirectoryContents, ExcludePaths: [environment.Windows32(@"Logs\CBS")],
                RequiresElevation: true, ScanNote: "已排除 CBS 服务日志"),
            new("perf-logs", "性能日志 PerfLogs", CleanupCategory.SystemLogs, CleanupRisk.Low,
                "性能监视器输出的日志目录。",
                "i-doc", Fixed(environment.Under(drive, "PerfLogs")),
                CleanerKind.DirectoryContents, RequiresElevation: true),
            new("wmi-rtbackup", "WMI 实时备份日志", CleanupCategory.SystemLogs, CleanupRisk.Low,
                "WMI 实时事件日志；被 WMI 服务占用时会跳过。",
                "i-doc", Fixed(environment.Windows32(@"System32\LogFiles\WMI\RtBackup")),
                CleanerKind.DirectoryContents, RequiresElevation: true),

            // ── G3 升级与安装残留 ────────────────────────────────────────────────
            new("upgrade-bt", "升级临时文件 $WINDOWS.~BT", CleanupCategory.SystemUpdate, CleanupRisk.Medium,
                "Windows 升级过程中的临时文件；删除后无法回滚本次升级。升级进行中会大量跳过，请稍后再试。",
                "i-window", Fixed(environment.Under(drive, "$WINDOWS.~BT")),
                CleanerKind.DirectoryContents, Tier: ScanTier.Slow, RequiresElevation: true),
            new("upgrade-ws", "升级临时文件 $WINDOWS.~WS", CleanupCategory.SystemUpdate, CleanupRisk.Medium,
                "Windows 升级使用的临时工作目录；删除后无法回滚本次升级。",
                "i-window", Fixed(environment.Under(drive, "$WINDOWS.~WS")),
                CleanerKind.DirectoryContents, Tier: ScanTier.Slow, RequiresElevation: true),
            new("get-current", "升级工具残留 $GetCurrent", CleanupCategory.SystemUpdate, CleanupRisk.Low,
                "媒体创建工具与升级助手留下的临时目录。",
                "i-window", Fixed(environment.Under(drive, "$GetCurrent")),
                CleanerKind.DirectoryContents, RequiresElevation: true),
            new("esd", "升级镜像缓存 ESD", CleanupCategory.SystemUpdate, CleanupRisk.Medium,
                "升级下载的 ESD 镜像文件；删除后再次升级需要重新下载。",
                "i-download", Fixed(environment.Under(drive, "ESD")),
                CleanerKind.DirectoryContents, Tier: ScanTier.Slow, RequiresElevation: true),
            new("win-system-temp", "系统临时目录 SystemTemp", CleanupCategory.TemporaryFiles, CleanupRisk.Low,
                "Windows 11 24H2 起的系统临时目录。",
                "i-file", Fixed(environment.Windows32("SystemTemp")),
                CleanerKind.DirectoryContents, MinimumAge: oldFileCutoff, RequiresElevation: true),
            new("retail-demo", "零售演示模式缓存", CleanupCategory.SystemUpdate, CleanupRisk.Low,
                "零售演示模式（RetailDemo）留下的内容。",
                "i-window", Fixed(environment.Under(programData, "Microsoft", "Windows", "RetailDemo")),
                CleanerKind.DirectoryContents, RequiresElevation: true),
            new("downloaded-program-files", "Downloaded Program Files", CleanupCategory.SystemUpdate, CleanupRisk.Low,
                "旧式 ActiveX / 插件的下载缓存目录。",
                "i-download", Fixed(environment.Windows32("Downloaded Program Files")),
                CleanerKind.DirectoryContents, RequiresElevation: true),
            new("winre-agent", "WinRE 代理残留 $WinREAgent", CleanupCategory.SystemFiles, CleanupRisk.Medium,
                "恢复环境更新过程的残留目录；仅展示占用，由系统管理。",
                "i-window", Fixed(environment.Under(drive, "$WinREAgent")), RequiresElevation: true),

            // ── G4 遥测与 Defender ──────────────────────────────────────────────
            new("diagnosis", "诊断遥测数据", CleanupCategory.SystemLogs, CleanupRisk.Low,
                "Windows 诊断与遥测的本地缓存（Diagnosis）。",
                "i-doc", Fixed(environment.Under(programData, "Microsoft", "Diagnosis")),
                CleanerKind.DirectoryContents, Tier: ScanTier.Slow, RequiresElevation: true),
            new("uso-shared", "更新会话缓存 USOShared", CleanupCategory.SystemUpdate, CleanupRisk.Low,
                "更新会话编排（USO）共享日志与状态。",
                "i-refresh", Fixed(environment.Under(programData, "Microsoft", "USOShared")),
                CleanerKind.DirectoryContents, RequiresElevation: true),
            new("uso-private", "更新会话缓存 USOPrivate", CleanupCategory.SystemUpdate, CleanupRisk.Low,
                "更新会话编排（USO）私有状态。",
                "i-refresh", Fixed(environment.Under(programData, "Microsoft", "USOPrivate")),
                CleanerKind.DirectoryContents, RequiresElevation: true),
            new("defender-history", "Defender 扫描历史", CleanupCategory.SystemLogs, CleanupRisk.Medium,
                "Defender 扫描历史；受 Tamper Protection 保护，仅展示占用。",
                "i-alert", Fixed(environment.Under(programData, "Microsoft", "Windows Defender", "Scans", "History")),
                RequiresElevation: true),

            // ── G5 显卡与游戏缓存 ───────────────────────────────────────────────
            new("nvidia-shader-user", "NVIDIA 着色器缓存", CleanupCategory.GraphicsAndGameCache, CleanupRisk.Low,
                "NVIDIA 驱动生成的 DX / GL 着色器缓存，游戏会按需重建。",
                "i-chip", Multiple(
                    environment.Under(local, "NVIDIA", "DXCache"),
                    environment.Under(local, "NVIDIA", "GLCache")),
                CleanerKind.DirectoryContents),
            new("nvidia-nv-cache", "NVIDIA 安装缓存", CleanupCategory.GraphicsAndGameCache, CleanupRisk.Low,
                "NVIDIA 驱动安装与下载缓存。",
                "i-chip", Fixed(environment.Under(programData, "NVIDIA Corporation", "NV_Cache")),
                CleanerKind.DirectoryContents, RequiresElevation: true),
            new("amd-shader", "AMD 着色器缓存", CleanupCategory.GraphicsAndGameCache, CleanupRisk.Low,
                "AMD 驱动生成的 DX / GL 着色器缓存。",
                "i-chip", Multiple(
                    environment.Under(local, "AMD", "DxCache"),
                    environment.Under(local, "AMD", "GLCache")),
                CleanerKind.DirectoryContents),
            new("intel-shader", "Intel 着色器缓存", CleanupCategory.GraphicsAndGameCache, CleanupRisk.Low,
                "Intel 显卡驱动生成的着色器缓存。",
                "i-chip", Fixed(environment.Under(local, "Intel", "ShaderCache")),
                CleanerKind.DirectoryContents),
            new("d3d-shader-cache", "Direct3D 着色器缓存", CleanupCategory.GraphicsAndGameCache, CleanupRisk.Low,
                "D3DSCache：Direct3D 编译后的着色器缓存。",
                "i-chip", Fixed(environment.Under(local, "D3DSCache")),
                CleanerKind.DirectoryContents),
            new("steam-shadercache", "Steam 着色器缓存", CleanupCategory.GraphicsAndGameCache, CleanupRisk.Low,
                "Steam 为各游戏预编译的着色器缓存；清理后首次运行游戏会重新编译。",
                "i-chip", ToolPath(toolchain, "steam", "steamapps", "shadercache"),
                CleanerKind.DirectoryContents, Tier: ScanTier.Slow, ScanTimeout: largeTreeTimeout),
            new("steam-depot-appcache", "Steam 下载与网页缓存", CleanupCategory.GraphicsAndGameCache, CleanupRisk.Low,
                "Steam 的 depot 清单、应用缓存与内置浏览器缓存。",
                "i-chip", Multiple(
                    ToolPath(toolchain, "steam", "steamapps", "depotcache"),
                    ToolPath(toolchain, "steam", "appcache"),
                    Fixed(environment.Under(local, "Steam", "htmlcache"))),
                CleanerKind.DirectoryContents, Tier: ScanTier.Slow, ScanTimeout: largeTreeTimeout),
            new("epic-webcache", "Epic 启动器网页缓存", CleanupCategory.GraphicsAndGameCache, CleanupRisk.Low,
                "Epic Games 启动器的内置浏览器缓存。",
                "i-chip", ToolPath(toolchain, "epic", "Saved", "webcache"),
                CleanerKind.DirectoryContents),
            new("gog-battlenet-cache", "GOG / Battle.net 缓存", CleanupCategory.GraphicsAndGameCache, CleanupRisk.Low,
                "GOG Galaxy 与 Battle.net 的网页与下载缓存（路径为候选值，命中才显示）。",
                "i-chip", Multiple(
                    ToolPath(toolchain, "gog", "webcache"),
                    ToolPath(toolchain, "battlenet", "Cache")),
                CleanerKind.DirectoryContents),

            // ── G7 浏览器家族 ───────────────────────────────────────────────────
            new("browser-cache", "Edge / Chrome 缓存", CleanupCategory.BrowserCache, CleanupRisk.Low,
                "Edge 与 Chrome 的网页、代码、GPU 与着色器缓存，不包含历史记录、登录信息或收藏夹。",
                "i-globe", Fixed(browserPaths), CleanerKind.DirectoryContents,
                LocationOverride: browserPaths.Length == 0 ? "Edge / Chrome 网页缓存" : null,
                PathSource: _ => "目录探测"),
            new("firefox-cache", "Firefox 缓存", CleanupCategory.BrowserCache, CleanupRisk.Low,
                "Firefox 各配置文件的网页、启动与着色器缓存，不含历史记录与登录数据。",
                "i-globe", Fixed(firefoxPaths), CleanerKind.DirectoryContents,
                PathSource: _ => "目录探测"),
            new("webview2-cache", "WebView2 应用缓存", CleanupCategory.BrowserCache, CleanupRisk.Low,
                "使用 WebView2 的桌面应用（Teams、Office 加载项等）的网页与 GPU 缓存。",
                "i-globe", Fixed(webViewPaths), CleanerKind.DirectoryContents,
                PathSource: _ => "目录探测"),
            new("chromium-family-cache", "Brave / Vivaldi / Opera 缓存", CleanupCategory.BrowserCache, CleanupRisk.Low,
                "其它 Chromium 内核浏览器的网页缓存，不含历史记录与登录数据。",
                "i-globe", Fixed(chromiumFamilyPaths), CleanerKind.DirectoryContents,
                PathSource: _ => "目录探测"),
            new("cn-browser-cache", "国产浏览器缓存", CleanupCategory.BrowserCache, CleanupRisk.Low,
                "360 / QQ / 搜狗浏览器的网页缓存（候选路径，命中才显示，仍需真机确认）。",
                "i-globe", Fixed(cnBrowserPaths), CleanerKind.DirectoryContents,
                PathSource: _ => "候选路径"),
            new("inetcache", "旧版浏览器缓存 INetCache", CleanupCategory.BrowserCache, CleanupRisk.Low,
                "IE 模式与内嵌浏览器控件共用的网页缓存（INetCache）；不含 Cookie、历史记录与表单数据。",
                "i-globe", Fixed(environment.Under(local, "Microsoft", "Windows", "INetCache")),
                CleanerKind.DirectoryContents, Tier: ScanTier.Slow, ScanTimeout: largeTreeTimeout),

            // ── G10 Electron 与桌面应用 ─────────────────────────────────────────
            new("discord-cache", "Discord 缓存", CleanupCategory.ApplicationData, CleanupRisk.Low,
                "Discord 客户端的网页、代码与 GPU 缓存，不影响聊天记录。",
                "i-chat", Fixed(ChromiumCacheDirectories(environment.Under(roaming, "discord"))),
                CleanerKind.DirectoryContents),
            new("slack-cache", "Slack 缓存", CleanupCategory.ApplicationData, CleanupRisk.Low,
                "Slack 客户端的网页、代码与 GPU 缓存，不影响聊天记录。",
                "i-chat", Fixed(ChromiumCacheDirectories(environment.Under(roaming, "Slack"))),
                CleanerKind.DirectoryContents),
            new("teams-classic-cache", "Teams 经典版缓存", CleanupCategory.ApplicationData, CleanupRisk.Low,
                "Teams 经典版的网页、代码与 GPU 缓存。",
                "i-chat", Fixed(ChromiumCacheDirectories(environment.Under(roaming, "Microsoft", "Teams"))),
                CleanerKind.DirectoryContents),
            new("teams-new-cache", "Teams 新版缓存", CleanupCategory.ApplicationData, CleanupRisk.Low,
                "新版 Teams 的本地缓存（LocalCache）。",
                "i-chat", Fixed(environment.Package("MSTeams_*", "LocalCache")),
                CleanerKind.DirectoryContents, Tier: ScanTier.Slow, ScanTimeout: largeTreeTimeout,
                AllowedRoots: Fixed(environment.Under(local, "Packages"))),
            new("office-file-cache", "Office 文档缓存", CleanupCategory.ApplicationData, CleanupRisk.Low,
                "Office 的文件缓存，删除后打开文档时会重新下载。",
                "i-doc", Fixed(environment.Under(local, "Microsoft", "Office", "16.0", "OfficeFileCache")),
                CleanerKind.DirectoryContents),
            new("onedrive-logs", "OneDrive 日志", CleanupCategory.ApplicationData, CleanupRisk.Low,
                "OneDrive 同步日志与安装日志。",
                "i-doc", Multiple(
                    environment.Under(local, "Microsoft", "OneDrive", "logs"),
                    environment.Under(local, "Microsoft", "OneDrive", "setup", "logs")),
                CleanerKind.DirectoryContents),
            new("rdp-cache", "远程桌面位图缓存", CleanupCategory.ApplicationData, CleanupRisk.Low,
                "远程桌面连接的位图缓存，删除后重连会重新生成。",
                "i-window", Fixed(environment.Under(local, "Microsoft", "Terminal Server Client", "Cache")),
                CleanerKind.DirectoryContents),
            // Electron 应用（含 VS Code 分支）都把 Chromium 缓存放在 userData 下的同构目录里，
            // 命中才显示；不触碰聊天记录、工作区与设置。
            new("signal-cache", "Signal 缓存", CleanupCategory.ApplicationData, CleanupRisk.Low,
                "Signal 桌面版的网页与 GPU 缓存，不影响聊天记录与已加密的本地数据库。",
                "i-chat", Fixed(ChromiumCacheDirectories(environment.Under(roaming, "Signal"))),
                CleanerKind.DirectoryContents),
            new("whatsapp-cache", "WhatsApp 缓存", CleanupCategory.ApplicationData, CleanupRisk.Low,
                "WhatsApp 桌面版的网页与 GPU 缓存，不影响聊天记录（记录在手机端）。",
                "i-chat", Fixed(ChromiumCacheDirectories(environment.Under(roaming, "WhatsApp"))),
                CleanerKind.DirectoryContents),
            new("notion-cache", "Notion 缓存", CleanupCategory.ApplicationData, CleanupRisk.Low,
                "Notion 桌面版的网页与 GPU 缓存，删除后打开页面时会重新下载。",
                "i-doc", Fixed(ChromiumCacheDirectories(environment.Under(roaming, "Notion"))),
                CleanerKind.DirectoryContents),
            new("postman-cache", "Postman 缓存", CleanupCategory.ApplicationData, CleanupRisk.Low,
                "Postman 桌面版的网页与 GPU 缓存，不影响集合与历史记录。",
                "i-doc", Fixed(ChromiumCacheDirectories(environment.Under(roaming, "Postman"))),
                CleanerKind.DirectoryContents),
            new("figma-cache", "Figma 缓存", CleanupCategory.ApplicationData, CleanupRisk.Low,
                "Figma 桌面版的网页与 GPU 缓存，不影响本地草稿。",
                "i-image", Fixed(ChromiumCacheDirectories(environment.Under(roaming, "Figma"))),
                CleanerKind.DirectoryContents),
            new("obsidian-cache", "Obsidian 缓存", CleanupCategory.ApplicationData, CleanupRisk.Low,
                "Obsidian 的 GPU 与代码缓存，不影响笔记库本身。",
                "i-doc", Fixed(ChromiumCacheDirectories(environment.Under(roaming, "obsidian"))),
                CleanerKind.DirectoryContents),
            new("cursor-cache", "Cursor 缓存", CleanupCategory.PackageCache, CleanupRisk.Low,
                "Cursor（VS Code 分支）的扩展与网络缓存，不含设置、扩展本身与项目索引。",
                "i-db", Fixed(ChromiumCacheDirectories(environment.Under(roaming, "Cursor"))),
                CleanerKind.DirectoryContents),

            // ── G6 Store 应用与 Game Bar ────────────────────────────────────────
            new("store-tempstate", "Store 应用临时状态", CleanupCategory.GraphicsAndGameCache, CleanupRisk.Low,
                "UWP / Store 应用的 TempState 目录。",
                "i-folder", Fixed(environment.Package("*", "TempState")),
                CleanerKind.DirectoryContents, Tier: ScanTier.Slow, ScanTimeout: largeTreeTimeout,
                AllowedRoots: Fixed(environment.Under(local, "Packages"))),
            new("store-ac-temp", "Store 应用 AC 临时文件", CleanupCategory.GraphicsAndGameCache, CleanupRisk.Low,
                "UWP / Store 应用 AC\\Temp 目录。",
                "i-folder", Fixed(environment.Package("*", "AC", "Temp")),
                CleanerKind.DirectoryContents, Tier: ScanTier.Slow, ScanTimeout: largeTreeTimeout,
                AllowedRoots: Fixed(environment.Under(local, "Packages"))),
            new("store-localcache", "Store 应用本地缓存", CleanupCategory.GraphicsAndGameCache, CleanupRisk.Medium,
                "UWP / Store 应用的 LocalCache；个别应用会把本地数据放在这里，清理后可能丢失该应用的本地状态。",
                "i-folder", Fixed(environment.Package("*", "LocalCache")),
                CleanerKind.DirectoryContents, Tier: ScanTier.Slow, ScanTimeout: largeTreeTimeout,
                AllowedRoots: Fixed(environment.Under(local, "Packages"))),

            // ── G8 开发缓存补充 ─────────────────────────────────────────────────
            new("huggingface-cache", "HuggingFace 模型缓存", CleanupCategory.PackageCache, CleanupRisk.Medium,
                "下载的模型与数据集缓存；清理后再次使用需要重新下载。",
                "i-db", ToolPaths(toolchain, "huggingface"), CleanerKind.DirectoryContents,
                Tier: ScanTier.Slow, ScanTimeout: largeTreeTimeout),
            new("torch-cache", "PyTorch 缓存", CleanupCategory.PackageCache, CleanupRisk.Medium,
                "PyTorch Hub 与编译缓存；清理后需要重新下载或重新编译。",
                "i-db", ToolPaths(toolchain, "torch"), CleanerKind.DirectoryContents,
                Tier: ScanTier.Slow, ScanTimeout: largeTreeTimeout),
            new("conda-pkgs", "Conda 包缓存", CleanupCategory.PackageCache, CleanupRisk.Medium,
                "conda 已下载的包文件；清理后新建环境需要重新下载。",
                "i-db", Multiple(Tool(toolchain, "conda"), ToolPath(toolchain, "anaconda", "pkgs")),
                CleanerKind.DirectoryContents, Tier: ScanTier.Slow, ScanTimeout: largeTreeTimeout),
            new("composer-cache", "Composer 缓存", CleanupCategory.PackageCache, CleanupRisk.Low,
                "PHP Composer 的下载缓存。",
                "i-db", Multiple(Tool(toolchain, "composer"), Tool(toolchain, "composer-roaming")),
                CleanerKind.DirectoryContents),
            new("chocolatey-temp", "Chocolatey 日志与残留", CleanupCategory.PackageCache, CleanupRisk.Low,
                "Chocolatey 的日志与损坏包目录。",
                "i-db", Multiple(
                    Fixed(environment.Under(programData, "chocolatey", "logs")),
                    Fixed(environment.Under(programData, "chocolatey", "lib-bad")),
                    Fixed(environment.Under(temp, "chocolatey"))),
                CleanerKind.DirectoryContents, RequiresElevation: true),
            new("scoop-cache", "Scoop 缓存", CleanupCategory.PackageCache, CleanupRisk.Low,
                "Scoop 下载的安装包缓存。",
                "i-db", ToolPath(toolchain, "scoop", "cache"), CleanerKind.DirectoryContents),
            new("winget-localstate", "winget 本地状态", CleanupCategory.PackageCache, CleanupRisk.Low,
                "winget（DesktopAppInstaller）的本地缓存与日志。",
                "i-db", Fixed(environment.Package("Microsoft.DesktopAppInstaller_8wekyb3d8bbwe", "LocalState")),
                CleanerKind.DirectoryContents),
            new("vcpkg-downloads", "vcpkg 下载与构建缓存", CleanupCategory.PackageCache, CleanupRisk.Medium,
                "vcpkg 下载的源码包与中间构建目录；清理后需要重新下载与编译。",
                "i-db", Multiple(
                    ToolPath(toolchain, "vcpkg", "downloads"),
                    ToolPath(toolchain, "vcpkg", "buildtrees")),
                CleanerKind.DirectoryContents, Tier: ScanTier.Slow, ScanTimeout: largeTreeTimeout),
            new("msys2-pacman-cache", "MSYS2 pacman 包缓存", CleanupCategory.PackageCache, CleanupRisk.Low,
                "MSYS2 已下载的 pacman 包。",
                "i-db", ToolPath(toolchain, "msys2", "var", "cache", "pacman", "pkg"),
                CleanerKind.DirectoryContents, Tier: ScanTier.Slow),

            // ── G9 引擎与移动开发 ───────────────────────────────────────────────
            new("unity-cache", "Unity 缓存", CleanupCategory.PackageCache, CleanupRisk.Low,
                "Unity 编辑器的资源导入与下载缓存。",
                "i-db", ToolPaths(toolchain, "unity"), CleanerKind.DirectoryContents,
                Tier: ScanTier.Slow, ScanTimeout: largeTreeTimeout),
            new("unreal-ddc", "Unreal 派生数据缓存", CleanupCategory.PackageCache, CleanupRisk.Low,
                "Unreal Engine 的 DerivedDataCache；清理后打开项目会重新生成。",
                "i-db", ToolPaths(toolchain, "unreal"), CleanerKind.DirectoryContents,
                Tier: ScanTier.Slow, ScanTimeout: largeTreeTimeout),
            new("android-sdk-temp", "Android SDK 临时目录", CleanupCategory.PackageCache, CleanupRisk.Low,
                "Android SDK 下载与解压的临时目录。",
                "i-db", ToolPath(toolchain, "androidSdk", ".temp"), CleanerKind.DirectoryContents),
            new("android-avd", "Android 模拟器镜像", CleanupCategory.ApplicationData, CleanupRisk.Medium,
                "Android 虚拟设备镜像；仅展示占用，删除会丢失模拟器数据。",
                "i-db", Fixed(environment.Under(profile, ".android", "avd")), Tier: ScanTier.Slow),
            new("rustup-toolchains", "Rust 工具链", CleanupCategory.ApplicationData, CleanupRisk.Medium,
                "rustup 已安装的工具链；仅展示占用，请用 rustup 管理。",
                "i-db", Fixed(environment.Under(profile, ".rustup", "toolchains")), Tier: ScanTier.Slow),

            // ── G13 开发缓存（保留 + 补齐） ─────────────────────────────────────
            new("nuget-global", "NuGet 全局包缓存", CleanupCategory.PackageCache, CleanupRisk.Medium,
                "已还原的 NuGet 包副本；清理后项目下次生成会重新下载依赖。",
                "i-db", Fixed(nugetPackages), CleanerKind.Command,
                Command: new("dotnet", ["nuget", "locals", "global-packages", "--clear"],
                    "NuGet global-packages 已通过官方命令清理。"),
                ScanTimeout: largeTreeTimeout,
                CheckLoadedModules: true, PathSource: _ => toolchain.ContainsKey("nuget") ? toolchain["nuget"].Source : "默认路径"),
            new("nuget-http", "NuGet HTTP 缓存", CleanupCategory.PackageCache, CleanupRisk.Low,
                "NuGet 下载缓存，可通过 dotnet 官方命令安全重建。",
                "i-db", Fixed(environment.Under(local, "NuGet", "v3-cache")), CleanerKind.Command,
                Command: new("dotnet", ["nuget", "locals", "http-cache", "--clear"],
                    "NuGet http-cache 已通过官方命令清理。")),
            new("nuget-temp", "NuGet 临时缓存", CleanupCategory.TemporaryFiles, CleanupRisk.Low,
                "NuGet 操作产生的临时内容，可通过 dotnet 官方命令清理。",
                "i-db", Fixed(environment.Under(temp, "NuGetScratch")), CleanerKind.Command,
                Command: new("dotnet", ["nuget", "locals", "temp", "--clear"],
                    "NuGet temp 已通过官方命令清理。")),
            new("nuget-plugins", "NuGet 插件缓存", CleanupCategory.PackageCache, CleanupRisk.Low,
                "NuGet 凭据与插件进程缓存，可通过 dotnet 官方命令重建。",
                "i-db", Fixed(environment.Under(local, "NuGet", "plugins-cache")), CleanerKind.Command,
                Command: new("dotnet", ["nuget", "locals", "plugins-cache", "--clear"],
                    "NuGet plugins-cache 已通过官方命令清理。")),
            new("user-temp", "过期临时文件", CleanupCategory.TemporaryFiles, CleanupRisk.Low,
                "超过 7 天未修改的用户临时文件；正在使用或无权限的文件会跳过。",
                "i-file", Fixed(temp), CleanerKind.DirectoryContents, MinimumAge: oldFileCutoff),
            new("npm-cache", "npm 缓存", CleanupCategory.PackageCache, CleanupRisk.Low,
                "npm 下载缓存；清理后安装依赖时会重新下载。",
                "i-db", Fixed(npmCache), CleanerKind.Command,
                Command: new("npm", ["cache", "clean", "--force"], "npm 缓存已通过官方命令清理。"),
                PathSource: _ => toolchain.TryGetValue("npm", out var resolution) ? resolution.Source : null),
            new("pnpm-store", "pnpm 存储", CleanupCategory.PackageCache, CleanupRisk.Medium,
                "pnpm 内容寻址存储；建议用 pnpm store prune 只删未被引用的包，因此本项清理后大小可能不下降。",
                "i-db", ToolPaths(toolchain, "pnpm"), CleanerKind.Command,
                Command: new("pnpm", ["store", "prune"], "pnpm 存储已通过官方命令清理（只删除未被引用的包）。"),
                PathSource: _ => toolchain.TryGetValue("pnpm", out var resolution) ? resolution.Source : null),
            new("yarn-cache", "yarn 缓存", CleanupCategory.PackageCache, CleanupRisk.Low,
                "yarn 下载缓存；清理后安装依赖时会重新下载。",
                "i-db", ToolPaths(toolchain, "yarn"), CleanerKind.Command,
                Command: new("yarn", ["cache", "clean"], "yarn 缓存已通过官方命令清理。"),
                PathSource: _ => toolchain.TryGetValue("yarn", out var resolution) ? resolution.Source : null),
            new("pip-cache", "pip 缓存", CleanupCategory.PackageCache, CleanupRisk.Low,
                "pip 下载的 wheel 与源码包缓存。",
                "i-db", ToolPaths(toolchain, "pip"), CleanerKind.DirectoryContents,
                PathSource: _ => toolchain.TryGetValue("pip", out var resolution) ? resolution.Source : null),
            new("uv-cache", "uv 缓存", CleanupCategory.PackageCache, CleanupRisk.Low,
                "uv 的包与构建缓存。",
                "i-db", ToolPaths(toolchain, "uv"), CleanerKind.DirectoryContents),
            new("cargo-registry", "Cargo 注册表缓存", CleanupCategory.PackageCache, CleanupRisk.Medium,
                "Cargo 下载的 crate 压缩包缓存；清理后构建需要重新下载。",
                "i-db", ToolPaths(toolchain, "cargo"), CleanerKind.DirectoryContents,
                Tier: ScanTier.Slow, ScanTimeout: largeTreeTimeout),
            new("go-modcache", "Go 模块缓存", CleanupCategory.PackageCache, CleanupRisk.Medium,
                "Go module 缓存；go clean -modcache 会清空整个模块缓存，清理后需要重新下载。",
                "i-db", ToolPaths(toolchain, "go"), CleanerKind.DirectoryContents,
                Tier: ScanTier.Slow, ScanTimeout: largeTreeTimeout),
            new("gradle-caches", "Gradle 缓存", CleanupCategory.PackageCache, CleanupRisk.Medium,
                "Gradle 依赖与构建缓存；清理后首次构建会重新下载依赖。",
                "i-db", ToolPaths(toolchain, "gradle"), CleanerKind.DirectoryContents,
                Tier: ScanTier.Slow, ScanTimeout: largeTreeTimeout),
            new("maven-repository", "Maven 本地仓库", CleanupCategory.PackageCache, CleanupRisk.Medium,
                "Maven 本地仓库；清理后构建需要重新下载依赖。",
                "i-db", ToolPaths(toolchain, "maven"), CleanerKind.DirectoryContents,
                Tier: ScanTier.Slow, ScanTimeout: largeTreeTimeout),
            new("vscode-cache", "VS Code 缓存", CleanupCategory.PackageCache, CleanupRisk.Low,
                "VS Code 的扩展与网络缓存（不含设置与扩展本身）。",
                "i-db", Multiple(
                    Fixed(environment.Under(roaming, "Code", "Cache")),
                    Fixed(environment.Under(roaming, "Code", "CachedData")),
                    Fixed(environment.Under(roaming, "Code", "logs"))),
                CleanerKind.DirectoryContents),
            new("jetbrains-caches", "JetBrains 索引缓存", CleanupCategory.PackageCache, CleanupRisk.Medium,
                "JetBrains IDE 的索引与缓存；清理后首次打开项目会重新建立索引。",
                "i-db", Fixed(FindJetBrainsCaches(local)), CleanerKind.DirectoryContents,
                Tier: ScanTier.Slow, ScanTimeout: largeTreeTimeout, PathSource: _ => "目录探测"),

            // ── G11 OEM / 驱动安装残留（仅分析） ────────────────────────────────
            new("oem-intel", "Intel 安装残留", CleanupCategory.SystemFiles, CleanupRisk.Medium,
                "Intel 驱动安装解压目录；仅展示占用，确认无用后请手动删除。",
                "i-chip", Fixed(environment.Under(drive, "Intel")), Tier: ScanTier.Slow),
            new("oem-amd", "AMD 安装残留", CleanupCategory.SystemFiles, CleanupRisk.Medium,
                "AMD 驱动安装解压目录；仅展示占用。",
                "i-chip", Fixed(environment.Under(drive, "AMD")), Tier: ScanTier.Slow),
            new("oem-nvidia", "NVIDIA 安装残留", CleanupCategory.SystemFiles, CleanupRisk.Medium,
                "NVIDIA 驱动安装解压目录；仅展示占用。",
                "i-chip", Fixed(environment.Under(drive, "NVIDIA")), Tier: ScanTier.Slow),
            new("oem-swsetup", "OEM 预装软件残留", CleanupCategory.SystemFiles, CleanupRisk.Medium,
                "厂商预装软件安装源（SWSetup）；仅展示占用。",
                "i-window", Fixed(environment.Under(drive, "SWSetup")), Tier: ScanTier.Slow),
            new("oem-inetpub", "IIS 日志 inetpub", CleanupCategory.SystemFiles, CleanupRisk.Medium,
                "IIS 站点日志；仅展示占用，请用 IIS 管理工具清理。",
                "i-doc", Fixed(environment.Under(drive, "inetpub", "logs")), Tier: ScanTier.Slow),

            // ── G12 系统分析档（有代价，仅分析） ────────────────────────────────
            new("winsxs", "组件存储 WinSxS", CleanupCategory.SystemFiles, CleanupRisk.High,
                "组件存储占用。提权时使用 DISM 官方数字；未提权或分析失败时回退目录扫描，数字含硬链接、偏大。",
                "i-window", Fixed(environment.Windows32("WinSxS")),
                ScanKind: ScanKind.DismAnalyze, Tier: ScanTier.Slow, RequiresElevation: true,
                ScanNote: "仅展示，不提供清理入口",
                ScanLabel: "组件存储 WinSxS（DISM 分析）"),
            new("windows-installer", "Windows Installer 缓存", CleanupCategory.SystemFiles, CleanupRisk.High,
                "MSI 安装缓存；删除后无法修复或卸载对应软件。",
                "i-db", Fixed(environment.Windows32("Installer")), Tier: ScanTier.Slow),
            new("package-cache", "Visual Studio 包缓存", CleanupCategory.SystemFiles, CleanupRisk.Medium,
                "VS / MSI 安装源缓存；删除后修复安装需要重新下载。",
                "i-db", Fixed(environment.Under(programData, "Package Cache")), Tier: ScanTier.Slow),
            new("search-index", "Windows 搜索索引", CleanupCategory.SystemFiles, CleanupRisk.Medium,
                "Windows.edb 搜索索引；仅展示占用，请在索引选项中重建。",
                "i-db", Fixed(environment.Under(programData, "Microsoft", "Search", "Data")), Tier: ScanTier.Slow),
            new("font-cache", "字体缓存服务数据", CleanupCategory.SystemFiles, CleanupRisk.Medium,
                "FontCache 服务缓存；需停止服务后才能清理。",
                "i-doc", Fixed(environment.Under(windows, "ServiceProfiles", "LocalService", "AppData", "Local", "FontCache")),
                Tier: ScanTier.Slow, RequiresElevation: true),
            new("vss-shadow", "卷影副本与还原点", CleanupCategory.SystemFiles, CleanupRisk.High,
                "卷影副本存储占用（vssadmin 数字）；请用系统保护设置管理。",
                "i-clock", Empty, ScanKind: ScanKind.VssQuery, Tier: ScanTier.Slow, RequiresElevation: true,
                LocationOverride: "vssadmin list shadowstorage", ScanNote: "仅展示，不提供清理入口",
                ScanLabel: "卷影副本与还原点（vssadmin 查询）"),
            new("cbs-logs", "CBS 服务日志", CleanupCategory.SystemLogs, CleanupRisk.Medium,
                "组件安装服务日志；被服务占用时会跳过。",
                "i-doc", Fixed(environment.Windows32(@"Logs\CBS")), Tier: ScanTier.Slow, RequiresElevation: true),
            new("etl-logs", "ETL 跟踪日志", CleanupCategory.SystemLogs, CleanupRisk.Medium,
                "系统与驱动的 ETL 跟踪日志总量。",
                "i-doc", Fixed(environment.Windows32(@"System32\LogFiles")), IncludePatterns: ["*.etl"],
                ScanKind: ScanKind.FilePattern, Tier: ScanTier.Slow, RequiresElevation: true),
            new("wsl-docker-vhdx", "WSL / Docker 虚拟磁盘", CleanupCategory.SystemFiles, CleanupRisk.Medium,
                "WSL 与 Docker 的 ext4.vhdx 磁盘镜像（只统计文件大小，不调用 wsl.exe）。",
                "i-db", Multiple(
                    environment.Package("*", "LocalState", "ext4.vhdx"),
                    environment.Under(local, "Docker", "wsl", "*", "data", "ext4.vhdx")),
                ScanKind: ScanKind.SingleFile, Tier: ScanTier.Slow),
            new("swapfile", "交换文件 swapfile.sys", CleanupCategory.SystemFiles, CleanupRisk.Medium,
                "与页面文件同族的交换文件，仅展示占用。",
                "i-db", Fixed(environment.Under(drive, "swapfile.sys")), RequiresElevation: true),
            new("windows-old", "旧版系统 Windows.old", CleanupCategory.SystemFiles, CleanupRisk.High,
                "系统升级备份，删除后无法回滚；应使用 Windows 设置管理。",
                "i-window", Fixed(environment.Under(drive, "Windows.old")),
                Tier: ScanTier.Slow, RequiresElevation: true, IsProtected: true),
            new("hiberfil", "休眠文件 hiberfil.sys", CleanupCategory.SystemFiles, CleanupRisk.Medium,
                "系统休眠镜像，仅展示占用；需要通过 powercfg 管理。",
                "i-moon", Fixed(environment.Under(drive, "hiberfil.sys")),
                RequiresElevation: true, IsProtected: true),
            new("pagefile", "页面文件 pagefile.sys", CleanupCategory.SystemFiles, CleanupRisk.Medium,
                "虚拟内存交换文件，仅展示占用，不可直接清理。",
                "i-db", Fixed(environment.Under(drive, "pagefile.sys")),
                RequiresElevation: true, IsProtected: true),
            new("memory-dump", "系统内存转储", CleanupCategory.SystemFiles, CleanupRisk.Medium,
                "蓝屏诊断转储，仅展示占用；确认无需排障后请使用 Windows 设置清理。",
                "i-chip", Fixed(environment.Windows32("MEMORY.DMP")),
                RequiresElevation: true, IsProtected: true),
            new("codex-data", "Codex 会话记录", CleanupCategory.ApplicationData, CleanupRisk.High,
                "仅统计 Codex 活动与归档会话。勾选后将永久删除全部会话文件；检测到 Codex 正在运行时会整项跳过。",
                "i-chat", Multiple(
                    environment.Under(profile, ".codex", "sessions"),
                    environment.Under(profile, ".codex", "archived_sessions")),
                CleanerKind.CodexConversations, LocationOverride: environment.Under(profile, ".codex")),
            new("recycle-bin", "回收站", CleanupCategory.RecycleBin, CleanupRisk.Medium,
                "清空后文件无法从回收站恢复，执行前必须单独确认。",
                "i-trash", Fixed(environment.Under(drive, "$Recycle.Bin")),
                CleanerKind.RecycleBin, LocationOverride: $"{drive}$Recycle.Bin")
        ];
    }

    private static Func<IReadOnlyDictionary<string, PathResolution>, IReadOnlyList<string>> Fixed(params string[] paths) =>
        _ => paths;

    /// <summary>
    /// 把若干路径来源合并成一组：字面路径、单个路径或 <see cref="Fixed"/>/<see cref="ToolPath"/> 这样的分组。
    /// 探测不到的来源为 <c>null</c>，按空集合处理。
    /// </summary>
    private static Func<IReadOnlyDictionary<string, PathResolution>, IReadOnlyList<string>> Multiple(
        params object?[] sources) =>
        toolchain => sources
            .SelectMany(source => source switch
            {
                Func<IReadOnlyDictionary<string, PathResolution>, IReadOnlyList<string>> group => group(toolchain),
                string path => (IReadOnlyList<string>)[path],
                IReadOnlyList<string> paths => paths,
                _ => []
            })
            .ToArray();

    private static Func<IReadOnlyDictionary<string, PathResolution>, IReadOnlyList<string>> Empty => _ => [];

    private static string? Tool(IReadOnlyDictionary<string, PathResolution> toolchain, string key) =>
        toolchain.TryGetValue(key, out var resolution) ? resolution.Path : null;

    private static Func<IReadOnlyDictionary<string, PathResolution>, IReadOnlyList<string>> ToolPath(
        IReadOnlyDictionary<string, PathResolution> toolchain,
        string key,
        params string[] segments)
    {
        var root = Tool(toolchain, key);
        return _ => string.IsNullOrWhiteSpace(root)
            ? []
            : [Path.Combine([root, .. segments])];
    }

    /// <summary>探测到的根目录本身就是一个目标（如 npm 缓存、Steam 库）。</summary>
    private static Func<IReadOnlyDictionary<string, PathResolution>, IReadOnlyList<string>> ToolPaths(
        IReadOnlyDictionary<string, PathResolution> toolchain,
        string key)
    {
        var root = Tool(toolchain, key);
        return _ => string.IsNullOrWhiteSpace(root) ? [] : [root];
    }

    private static string[] ChromiumCacheDirectories(string profileRoot)
    {
        if (!Directory.Exists(profileRoot))
        {
            return [];
        }

        var names = new[] { "Cache", "Code Cache", "GPUCache", "DawnCache", "DawnGraphiteCache", "Media Cache" };
        var paths = new List<string>();
        foreach (var name in names)
        {
            var candidate = Path.Combine(profileRoot, name);
            if (Directory.Exists(candidate))
            {
                paths.Add(candidate);
            }
        }

        return paths.ToArray();
    }

    private static string[] FindBrowserCacheDirectories(string localAppData)
    {
        var roots = new[]
        {
            Path.Combine(localAppData, "Microsoft", "Edge", "User Data"),
            Path.Combine(localAppData, "Google", "Chrome", "User Data")
        };

        return FindProfileCacheDirectories(roots, static profile => new[]
        {
            "Cache", "Code Cache", "GPUCache", "DawnCache", "DawnGraphiteCache", "Media Cache",
            "component_crx_cache", "Service Worker\\CacheStorage"
        });
    }

    private static string[] FindFirefoxCacheDirectories(string localAppData)
    {
        var root = Path.Combine(localAppData, "Mozilla", "Firefox", "Profiles");
        if (!Directory.Exists(root))
        {
            return [];
        }

        var paths = new List<string>();
        try
        {
            foreach (var profile in Directory.EnumerateDirectories(root))
            {
                foreach (var name in new[] { "cache2", "startupCache", "shader-cache" })
                {
                    var candidate = Path.Combine(profile, name);
                    if (Directory.Exists(candidate))
                    {
                        paths.Add(candidate);
                    }
                }
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
        }

        return paths.ToArray();
    }

    private static string[] FindWebView2CacheDirectories(string localAppData)
    {
        var root = Path.Combine(localAppData, "Microsoft", "EdgeWebView");
        if (!Directory.Exists(root))
        {
            return [];
        }

        var paths = new List<string>();
        try
        {
            foreach (var application in Directory.EnumerateDirectories(root))
            {
                foreach (var profile in Directory.EnumerateDirectories(application))
                {
                    foreach (var name in new[] { "Cache", "Code Cache", "GPUCache", "DawnCache", "DawnGraphiteCache" })
                    {
                        var candidate = Path.Combine(profile, name);
                        if (Directory.Exists(candidate))
                        {
                            paths.Add(candidate);
                        }
                    }
                }
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
        }

        return paths.ToArray();
    }

    private static string[] FindChromiumFamilyCacheDirectories(string localAppData)
    {
        var roots = new[]
        {
            Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data"),
            Path.Combine(localAppData, "Vivaldi", "User Data"),
            Path.Combine(localAppData, "Opera Software", "Opera Stable")
        };

        return FindProfileCacheDirectories(roots, static _ => ["Cache", "Code Cache", "GPUCache", "DawnCache"]);
    }

    private static string[] FindChineseBrowserCacheDirectories(
        IReadOnlyDictionary<string, PathResolution> toolchain)
    {
        var roots = new[]
        {
            Tool(toolchain, "cn360"),
            Tool(toolchain, "cnqq"),
            Tool(toolchain, "cnsogou")
        }.Where(root => !string.IsNullOrWhiteSpace(root)).Select(root => root!).ToArray();

        return FindProfileCacheDirectories(roots, static _ => ["Cache", "Code Cache", "GPUCache"]);
    }

    private static string[] FindJetBrainsCaches(string localAppData)
    {
        var root = Path.Combine(localAppData, "JetBrains");
        if (!Directory.Exists(root))
        {
            return [];
        }

        try
        {
            return Directory
                .EnumerateDirectories(root)
                .Select(directory => Path.Combine(directory, "caches"))
                .Where(Directory.Exists)
                .ToArray();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    private static string[] FindProfileCacheDirectories(
        IReadOnlyList<string> roots,
        Func<string, string[]> names)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots.Where(Directory.Exists))
        {
            AddIfExists(paths, Path.Combine(root, "ShaderCache"));
            AddIfExists(paths, Path.Combine(root, "GrShaderCache"));

            try
            {
                var profiles = Directory.EnumerateDirectories(root)
                    .Where(path => Path.GetFileName(path).Equals("Default", StringComparison.OrdinalIgnoreCase) ||
                                   Path.GetFileName(path).StartsWith("Profile ", StringComparison.OrdinalIgnoreCase) ||
                                   Path.GetFileName(path).Equals("Guest Profile", StringComparison.OrdinalIgnoreCase));

                foreach (var profile in profiles)
                {
                    foreach (var name in names(profile))
                    {
                        AddIfExists(paths, Path.Combine(profile, name));
                    }
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
