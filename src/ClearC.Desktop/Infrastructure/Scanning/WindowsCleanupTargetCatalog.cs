using ClearC.Desktop.I18n;
using ClearC.Core.Models;

namespace ClearC.Desktop.Infrastructure.Scanning;

internal interface ICleanupTargetCatalog
{
    /// <summary>静态定义（含候选路径），用于执行白名单与 id → 目标映射。</summary>
    IReadOnlyList<CleanupTargetDefinition> GetTargets();

    /// <summary>
    /// 扫描用：重新解析探测路径、展开通配目录，并剔除磁盘上不存在的目标，
    /// 避免列表里出现大量 0 字节假条目。
    /// <paramref name="driveScope"/> 为要扫描的盘符（如 ["C:", "D:"]）；
    /// <c>null</c> 或空 = 仅系统盘。系统盘使用完整目录，其他盘符只生成通用目标。
    /// </summary>
    Task<IReadOnlyList<CleanupTargetDefinition>> ResolveTargetsAsync(
        IReadOnlyList<string>? driveScope = null,
        CancellationToken cancellationToken = default);
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
    public IReadOnlyList<CleanupTargetDefinition> GetTargets()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        var toolchain = _pathResolver.ResolveDefaults();
        var rows = SystemRows(toolchain).Concat(ExtraDriveRows(FixedDriveRoots(excludeSystemDrive: true)));
        return Build(rows.ToArray(), toolchain, discoveredOnly: false);
    }

    public async Task<IReadOnlyList<CleanupTargetDefinition>> ResolveTargetsAsync(
        IReadOnlyList<string>? driveScope = null,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        var systemDrive = SystemDriveName();
        var includeSystem = driveScope is not { Count: > 0 }
            || driveScope.Any(drive => string.Equals(drive, systemDrive, StringComparison.OrdinalIgnoreCase));
        var extraDrives = (driveScope is { Count: > 0 }
                ? driveScope
                    .Where(drive => !string.Equals(drive, systemDrive, StringComparison.OrdinalIgnoreCase))
                    .ToArray()
                : [])
            .Intersect(FixedDriveRoots(excludeSystemDrive: false), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var toolchain = await _pathResolver.ResolveAsync(cancellationToken);
        IReadOnlyList<Row> rows = includeSystem ? SystemRows(toolchain) : [];
        foreach (var drive in extraDrives)
        {
            rows = rows.Concat(ExtraDriveRows([drive])).ToArray();
        }

        return Build(rows, toolchain, discoveredOnly: true);
    }

    private static IReadOnlyList<CleanupTargetDefinition> Build(
        IReadOnlyList<Row> rows,
        IReadOnlyDictionary<string, PathResolution> toolchain,
        bool discoveredOnly)
    {
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
                AppText.Format(row.Name, row.TextArgument),
                row.Category,
                row.Risk,
                AppText.Format(row.Description, row.TextArgument),
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
                row.ScanLabel,
                DisplayNameKey: row.Name, DescriptionKey: row.Description, TextArgument: row.TextArgument));
        }

        return targets;
    }

    private static bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>系统盘根目录（如 "C:"）。</summary>
    internal static string SystemDriveName() =>
        Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))?.TrimEnd('\\', '/') ?? "C:";

    /// <summary>本机全部固定磁盘根目录（如 ["C:", "D:"]）。</summary>
    internal static IReadOnlyList<string> FixedDriveRoots(bool excludeSystemDrive)
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        var systemDrive = SystemDriveName();
        var roots = new List<string>();
        try
        {
            roots.AddRange(DriveInfo.GetDrives()
                .Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady)
                .Select(drive => drive.Name.TrimEnd('\\', '/'))
                .Where(root => root.Length > 0));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return excludeSystemDrive
            ? roots.Where(root => !string.Equals(root, systemDrive, StringComparison.OrdinalIgnoreCase)).ToArray()
            : roots;
    }

    /// <summary>系统盘的完整清理目录（既有 G1–G13 全部目标）。</summary>
    private static IReadOnlyList<Row> SystemRows(IReadOnlyDictionary<string, PathResolution> toolchain) =>
        BuildRows(EnvironmentPaths.Create(), toolchain);

    /// <summary>
    /// 其他固定盘符的通用目标：临时目录、Windows.old、页面文件、驱动安装残留与回收站。
    /// 这些目录不依赖用户配置，路径全部以盘符为根。
    /// </summary>
    private static IReadOnlyList<Row> ExtraDriveRows(IReadOnlyList<string> driveRoots)
    {
        if (driveRoots.Count == 0)
        {
            return [];
        }

        var rows = new List<Row>();
        foreach (var driveRoot in driveRoots)
        {
            var letter = driveRoot.TrimEnd('\\', '/');
            var id = letter.ToLowerInvariant();
            // Path.Combine 需要 "D:\ 形态的根，否则 "D:" + "Temp" 会生成驱动器相对路径。
            var root = driveRoot.EndsWith('\\') ? driveRoot : driveRoot + '\\';
            rows.AddRange(
            [
                new($"{id}-temp", Language.TargetsDriveTempName, CleanupCategory.TemporaryFiles, CleanupRisk.Low,
                    Language.TargetsDriveTempDescription,
                    "i-file", Fixed(Path.Combine(root, "Temp"), Path.Combine(root, "tmp")),
                    CleanerKind.DirectoryContents, MinimumAge: TimeSpan.FromDays(7), TextArgument: letter),
                new($"{id}-windows-old", Language.TargetsDriveWindowsOldName, CleanupCategory.SystemFiles, CleanupRisk.High,
                    Language.TargetsDriveWindowsOldDescription,
                    "i-window", Fixed(Path.Combine(root, "Windows.old")),
                    Tier: ScanTier.Slow, IsProtected: true, TextArgument: letter),
                new($"{id}-pagefile", Language.TargetsDrivePagefileName, CleanupCategory.SystemFiles, CleanupRisk.Medium,
                    Language.TargetsDrivePagefileDescription,
                    "i-db", Fixed(Path.Combine(root, "pagefile.sys")), ScanKind: ScanKind.SingleFile, TextArgument: letter),
                new($"{id}-intel", Language.TargetsDriveIntelName, CleanupCategory.SystemFiles, CleanupRisk.Medium,
                    Language.TargetsDriveIntelDescription,
                    "i-chip", Fixed(Path.Combine(root, "Intel")), Tier: ScanTier.Slow, TextArgument: letter),
                new($"{id}-amd", Language.TargetsDriveAmdName, CleanupCategory.SystemFiles, CleanupRisk.Medium,
                    Language.TargetsDriveAmdDescription,
                    "i-chip", Fixed(Path.Combine(root, "AMD")), Tier: ScanTier.Slow, TextArgument: letter),
                new($"{id}-nvidia", Language.TargetsDriveNvidiaName, CleanupCategory.SystemFiles, CleanupRisk.Medium,
                    Language.TargetsDriveNvidiaDescription,
                    "i-chip", Fixed(Path.Combine(root, "NVIDIA")), Tier: ScanTier.Slow, TextArgument: letter),
                new($"{id}-swsetup", Language.TargetsDriveSwsetupName, CleanupCategory.SystemFiles, CleanupRisk.Medium,
                    Language.TargetsDriveSwsetupDescription,
                    "i-window", Fixed(Path.Combine(root, "SWSetup")), Tier: ScanTier.Slow, TextArgument: letter),
                new($"{id}-recycle-bin", Language.TargetsDriveRecycleBinName, CleanupCategory.RecycleBin, CleanupRisk.Medium,
                    Language.TargetsDriveRecycleBinDescription,
                    "i-trash", Fixed(Path.Combine(root, "$Recycle.Bin")),
                    CleanerKind.RecycleBin, LocationOverride: $"{root}$Recycle.Bin", TextArgument: letter)
            ]);
        }

        return rows;
    }

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
        string? ScanLabel = null,
        string? TextArgument = null);

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
            new("win-temp", Language.TargetsWinTempName, CleanupCategory.TemporaryFiles, CleanupRisk.Low,
                Language.TargetsWinTempDescription,
                "i-file", Fixed(environment.Windows32("Temp")),
                CleanerKind.DirectoryContents, MinimumAge: oldFileCutoff, RequiresElevation: true),
            new("wu-download", Language.TargetsWuDownloadName, CleanupCategory.SystemUpdate, CleanupRisk.Low,
                Language.TargetsWuDownloadDescription,
                "i-refresh", Fixed(environment.Windows32(@"SoftwareDistribution\Download")),
                CleanerKind.DirectoryContents, RequiresElevation: true),
            new("delivery-optimization", Language.TargetsDeliveryOptimizationName, CleanupCategory.SystemUpdate, CleanupRisk.Low,
                Language.TargetsDeliveryOptimizationDescription,
                "i-download", Fixed(environment.Under(programData, "Microsoft", "Windows", "DeliveryOptimization", "Cache")),
                CleanerKind.DirectoryContents, RequiresElevation: true),
            new("prefetch", Language.TargetsPrefetchName, CleanupCategory.SystemUpdate, CleanupRisk.Medium,
                Language.TargetsPrefetchDescription,
                "i-bolt", Fixed(environment.Windows32("Prefetch")),
                CleanerKind.DirectoryContents, RequiresElevation: true),
            new("thumbnail-icon-cache", Language.TargetsThumbnailIconCacheName, CleanupCategory.GraphicsAndGameCache, CleanupRisk.Low,
                Language.TargetsThumbnailIconCacheDescription,
                "i-image", Fixed(environment.Under(local, "Microsoft", "Windows", "Explorer")),
                CleanerKind.FilePattern,
                IncludePatterns: ["thumbcache_*.db", "iconcache_*.db"],
                ScanNote: AppText.Get(Language.RuntimeThumbnailFilesOnly)),

            // ── G2 Windows 日志与转储 ────────────────────────────────────────────
            new("wer-archive", Language.TargetsWerArchiveName, CleanupCategory.SystemLogs, CleanupRisk.Low,
                Language.TargetsWerArchiveDescription,
                "i-alert", Fixed(environment.Under(programData, "Microsoft", "Windows", "WER")),
                CleanerKind.DirectoryContents, RequiresElevation: true),
            new("wer-user", Language.TargetsWerUserName, CleanupCategory.SystemLogs, CleanupRisk.Low,
                Language.TargetsWerUserDescription,
                "i-alert", Fixed(environment.Under(local, "Microsoft", "Windows", "WER")),
                CleanerKind.DirectoryContents),
            new("crash-dumps", Language.TargetsCrashDumpsName, CleanupCategory.SystemLogs, CleanupRisk.Low,
                Language.TargetsCrashDumpsDescription,
                "i-chip", Fixed(environment.Under(local, "CrashDumps")),
                CleanerKind.DirectoryContents),
            new("minidump", Language.TargetsMinidumpName, CleanupCategory.SystemLogs, CleanupRisk.Low,
                Language.TargetsMinidumpDescription,
                "i-chip", Fixed(environment.Windows32("Minidump")),
                CleanerKind.DirectoryContents, RequiresElevation: true),
            new("live-kernel-reports", Language.TargetsLiveKernelReportsName, CleanupCategory.SystemLogs, CleanupRisk.Low,
                Language.TargetsLiveKernelReportsDescription,
                "i-doc", Fixed(environment.Windows32("LiveKernelReports")),
                CleanerKind.DirectoryContents, RequiresElevation: true),
            new("windows-panther", Language.TargetsWindowsPantherName, CleanupCategory.SystemLogs, CleanupRisk.Low,
                Language.TargetsWindowsPantherDescription,
                "i-doc", Fixed(environment.Windows32("Panther")),
                CleanerKind.DirectoryContents, RequiresElevation: true),
            new("windows-logs", Language.TargetsWindowsLogsName, CleanupCategory.SystemLogs, CleanupRisk.Low,
                Language.TargetsWindowsLogsDescription,
                "i-doc", Fixed(environment.Windows32("Logs")),
                CleanerKind.DirectoryContents, ExcludePaths: [environment.Windows32(@"Logs\CBS")],
                RequiresElevation: true, ScanNote: AppText.Get(Language.RuntimeExcludedCbs)),
            new("perf-logs", Language.TargetsPerfLogsName, CleanupCategory.SystemLogs, CleanupRisk.Low,
                Language.TargetsPerfLogsDescription,
                "i-doc", Fixed(environment.Under(drive, "PerfLogs")),
                CleanerKind.DirectoryContents, RequiresElevation: true),
            new("wmi-rtbackup", Language.TargetsWmiRtbackupName, CleanupCategory.SystemLogs, CleanupRisk.Low,
                Language.TargetsWmiRtbackupDescription,
                "i-doc", Fixed(environment.Windows32(@"System32\LogFiles\WMI\RtBackup")),
                CleanerKind.DirectoryContents, RequiresElevation: true),

            // ── G3 升级与安装残留 ────────────────────────────────────────────────
            new("upgrade-bt", Language.TargetsUpgradeBtName, CleanupCategory.SystemUpdate, CleanupRisk.Medium,
                Language.TargetsUpgradeBtDescription,
                "i-window", Fixed(environment.Under(drive, "$WINDOWS.~BT")),
                CleanerKind.DirectoryContents, Tier: ScanTier.Slow, RequiresElevation: true),
            new("upgrade-ws", Language.TargetsUpgradeWsName, CleanupCategory.SystemUpdate, CleanupRisk.Medium,
                Language.TargetsUpgradeWsDescription,
                "i-window", Fixed(environment.Under(drive, "$WINDOWS.~WS")),
                CleanerKind.DirectoryContents, Tier: ScanTier.Slow, RequiresElevation: true),
            new("get-current", Language.TargetsGetCurrentName, CleanupCategory.SystemUpdate, CleanupRisk.Low,
                Language.TargetsGetCurrentDescription,
                "i-window", Fixed(environment.Under(drive, "$GetCurrent")),
                CleanerKind.DirectoryContents, RequiresElevation: true),
            new("esd", Language.TargetsEsdName, CleanupCategory.SystemUpdate, CleanupRisk.Medium,
                Language.TargetsEsdDescription,
                "i-download", Fixed(environment.Under(drive, "ESD")),
                CleanerKind.DirectoryContents, Tier: ScanTier.Slow, RequiresElevation: true),
            new("win-system-temp", Language.TargetsWinSystemTempName, CleanupCategory.TemporaryFiles, CleanupRisk.Low,
                Language.TargetsWinSystemTempDescription,
                "i-file", Fixed(environment.Windows32("SystemTemp")),
                CleanerKind.DirectoryContents, MinimumAge: oldFileCutoff, RequiresElevation: true),
            new("retail-demo", Language.TargetsRetailDemoName, CleanupCategory.SystemUpdate, CleanupRisk.Low,
                Language.TargetsRetailDemoDescription,
                "i-window", Fixed(environment.Under(programData, "Microsoft", "Windows", "RetailDemo")),
                CleanerKind.DirectoryContents, RequiresElevation: true),
            new("downloaded-program-files", Language.TargetsDownloadedProgramFilesName, CleanupCategory.SystemUpdate, CleanupRisk.Low,
                Language.TargetsDownloadedProgramFilesDescription,
                "i-download", Fixed(environment.Windows32("Downloaded Program Files")),
                CleanerKind.DirectoryContents, RequiresElevation: true),
            new("winre-agent", Language.TargetsWinreAgentName, CleanupCategory.SystemFiles, CleanupRisk.Medium,
                Language.TargetsWinreAgentDescription,
                "i-window", Fixed(environment.Under(drive, "$WinREAgent")), RequiresElevation: true),

            // ── G4 遥测与 Defender ──────────────────────────────────────────────
            new("diagnosis", Language.TargetsDiagnosisName, CleanupCategory.SystemLogs, CleanupRisk.Low,
                Language.TargetsDiagnosisDescription,
                "i-doc", Fixed(environment.Under(programData, "Microsoft", "Diagnosis")),
                CleanerKind.DirectoryContents, Tier: ScanTier.Slow, RequiresElevation: true),
            new("uso-shared", Language.TargetsUsoSharedName, CleanupCategory.SystemUpdate, CleanupRisk.Low,
                Language.TargetsUsoSharedDescription,
                "i-refresh", Fixed(environment.Under(programData, "Microsoft", "USOShared")),
                CleanerKind.DirectoryContents, RequiresElevation: true),
            new("uso-private", Language.TargetsUsoPrivateName, CleanupCategory.SystemUpdate, CleanupRisk.Low,
                Language.TargetsUsoPrivateDescription,
                "i-refresh", Fixed(environment.Under(programData, "Microsoft", "USOPrivate")),
                CleanerKind.DirectoryContents, RequiresElevation: true),
            new("defender-history", Language.TargetsDefenderHistoryName, CleanupCategory.SystemLogs, CleanupRisk.Medium,
                Language.TargetsDefenderHistoryDescription,
                "i-alert", Fixed(environment.Under(programData, "Microsoft", "Windows Defender", "Scans", "History")),
                RequiresElevation: true),

            // ── G5 显卡与游戏缓存 ───────────────────────────────────────────────
            new("nvidia-shader-user", Language.TargetsNvidiaShaderUserName, CleanupCategory.GraphicsAndGameCache, CleanupRisk.Low,
                Language.TargetsNvidiaShaderUserDescription,
                "i-chip", Multiple(
                    environment.Under(local, "NVIDIA", "DXCache"),
                    environment.Under(local, "NVIDIA", "GLCache")),
                CleanerKind.DirectoryContents),
            new("nvidia-nv-cache", Language.TargetsNvidiaNvCacheName, CleanupCategory.GraphicsAndGameCache, CleanupRisk.Low,
                Language.TargetsNvidiaNvCacheDescription,
                "i-chip", Fixed(environment.Under(programData, "NVIDIA Corporation", "NV_Cache")),
                CleanerKind.DirectoryContents, RequiresElevation: true),
            new("amd-shader", Language.TargetsAmdShaderName, CleanupCategory.GraphicsAndGameCache, CleanupRisk.Low,
                Language.TargetsAmdShaderDescription,
                "i-chip", Multiple(
                    environment.Under(local, "AMD", "DxCache"),
                    environment.Under(local, "AMD", "GLCache")),
                CleanerKind.DirectoryContents),
            new("intel-shader", Language.TargetsIntelShaderName, CleanupCategory.GraphicsAndGameCache, CleanupRisk.Low,
                Language.TargetsIntelShaderDescription,
                "i-chip", Fixed(environment.Under(local, "Intel", "ShaderCache")),
                CleanerKind.DirectoryContents),
            new("d3d-shader-cache", Language.TargetsD3dShaderCacheName, CleanupCategory.GraphicsAndGameCache, CleanupRisk.Low,
                Language.TargetsD3dShaderCacheDescription,
                "i-chip", Fixed(environment.Under(local, "D3DSCache")),
                CleanerKind.DirectoryContents),
            new("steam-shadercache", Language.TargetsSteamShadercacheName, CleanupCategory.GraphicsAndGameCache, CleanupRisk.Low,
                Language.TargetsSteamShadercacheDescription,
                "i-chip", ToolPath(toolchain, "steam", "steamapps", "shadercache"),
                CleanerKind.DirectoryContents, Tier: ScanTier.Slow, ScanTimeout: largeTreeTimeout),
            new("steam-depot-appcache", Language.TargetsSteamDepotAppcacheName, CleanupCategory.GraphicsAndGameCache, CleanupRisk.Low,
                Language.TargetsSteamDepotAppcacheDescription,
                "i-chip", Multiple(
                    ToolPath(toolchain, "steam", "steamapps", "depotcache"),
                    ToolPath(toolchain, "steam", "appcache"),
                    Fixed(environment.Under(local, "Steam", "htmlcache"))),
                CleanerKind.DirectoryContents, Tier: ScanTier.Slow, ScanTimeout: largeTreeTimeout),
            new("epic-webcache", Language.TargetsEpicWebcacheName, CleanupCategory.GraphicsAndGameCache, CleanupRisk.Low,
                Language.TargetsEpicWebcacheDescription,
                "i-chip", ToolPath(toolchain, "epic", "Saved", "webcache"),
                CleanerKind.DirectoryContents),
            new("gog-battlenet-cache", Language.TargetsGogBattlenetCacheName, CleanupCategory.GraphicsAndGameCache, CleanupRisk.Low,
                Language.TargetsGogBattlenetCacheDescription,
                "i-chip", Multiple(
                    ToolPath(toolchain, "gog", "webcache"),
                    ToolPath(toolchain, "battlenet", "Cache")),
                CleanerKind.DirectoryContents),

            // ── G7 浏览器家族 ───────────────────────────────────────────────────
            new("browser-cache", Language.TargetsBrowserCacheName, CleanupCategory.BrowserCache, CleanupRisk.Low,
                Language.TargetsBrowserCacheDescription,
                "i-globe", Fixed(browserPaths), CleanerKind.DirectoryContents,
                LocationOverride: browserPaths.Length == 0 ? AppText.Get(Language.RuntimeBrowserCacheLocation) : null,
                PathSource: _ => AppText.Get(Language.RuntimeSourceDirectory)),
            new("firefox-cache", Language.TargetsFirefoxCacheName, CleanupCategory.BrowserCache, CleanupRisk.Low,
                Language.TargetsFirefoxCacheDescription,
                "i-globe", Fixed(firefoxPaths), CleanerKind.DirectoryContents,
                PathSource: _ => AppText.Get(Language.RuntimeSourceDirectory)),
            new("webview2-cache", Language.TargetsWebview2CacheName, CleanupCategory.BrowserCache, CleanupRisk.Low,
                Language.TargetsWebview2CacheDescription,
                "i-globe", Fixed(webViewPaths), CleanerKind.DirectoryContents,
                PathSource: _ => AppText.Get(Language.RuntimeSourceDirectory)),
            new("chromium-family-cache", Language.TargetsChromiumFamilyCacheName, CleanupCategory.BrowserCache, CleanupRisk.Low,
                Language.TargetsChromiumFamilyCacheDescription,
                "i-globe", Fixed(chromiumFamilyPaths), CleanerKind.DirectoryContents,
                PathSource: _ => AppText.Get(Language.RuntimeSourceDirectory)),
            new("cn-browser-cache", Language.TargetsCnBrowserCacheName, CleanupCategory.BrowserCache, CleanupRisk.Low,
                Language.TargetsCnBrowserCacheDescription,
                "i-globe", Fixed(cnBrowserPaths), CleanerKind.DirectoryContents,
                PathSource: _ => AppText.Get(Language.RuntimeSourceCandidate)),
            new("inetcache", Language.TargetsInetcacheName, CleanupCategory.BrowserCache, CleanupRisk.Low,
                Language.TargetsInetcacheDescription,
                "i-globe", Fixed(environment.Under(local, "Microsoft", "Windows", "INetCache")),
                CleanerKind.DirectoryContents, Tier: ScanTier.Slow, ScanTimeout: largeTreeTimeout),

            // ── G10 Electron 与桌面应用 ─────────────────────────────────────────
            new("discord-cache", Language.TargetsDiscordCacheName, CleanupCategory.ApplicationData, CleanupRisk.Low,
                Language.TargetsDiscordCacheDescription,
                "i-chat", Fixed(ChromiumCacheDirectories(environment.Under(roaming, "discord"))),
                CleanerKind.DirectoryContents),
            new("slack-cache", Language.TargetsSlackCacheName, CleanupCategory.ApplicationData, CleanupRisk.Low,
                Language.TargetsSlackCacheDescription,
                "i-chat", Fixed(ChromiumCacheDirectories(environment.Under(roaming, "Slack"))),
                CleanerKind.DirectoryContents),
            new("teams-classic-cache", Language.TargetsTeamsClassicCacheName, CleanupCategory.ApplicationData, CleanupRisk.Low,
                Language.TargetsTeamsClassicCacheDescription,
                "i-chat", Fixed(ChromiumCacheDirectories(environment.Under(roaming, "Microsoft", "Teams"))),
                CleanerKind.DirectoryContents),
            new("teams-new-cache", Language.TargetsTeamsNewCacheName, CleanupCategory.ApplicationData, CleanupRisk.Low,
                Language.TargetsTeamsNewCacheDescription,
                "i-chat", Fixed(environment.Package("MSTeams_*", "LocalCache")),
                CleanerKind.DirectoryContents, Tier: ScanTier.Slow, ScanTimeout: largeTreeTimeout,
                AllowedRoots: Fixed(environment.Under(local, "Packages"))),
            new("office-file-cache", Language.TargetsOfficeFileCacheName, CleanupCategory.ApplicationData, CleanupRisk.Low,
                Language.TargetsOfficeFileCacheDescription,
                "i-doc", Fixed(environment.Under(local, "Microsoft", "Office", "16.0", "OfficeFileCache")),
                CleanerKind.DirectoryContents),
            new("onedrive-logs", Language.TargetsOnedriveLogsName, CleanupCategory.ApplicationData, CleanupRisk.Low,
                Language.TargetsOnedriveLogsDescription,
                "i-doc", Multiple(
                    environment.Under(local, "Microsoft", "OneDrive", "logs"),
                    environment.Under(local, "Microsoft", "OneDrive", "setup", "logs")),
                CleanerKind.DirectoryContents),
            new("rdp-cache", Language.TargetsRdpCacheName, CleanupCategory.ApplicationData, CleanupRisk.Low,
                Language.TargetsRdpCacheDescription,
                "i-window", Fixed(environment.Under(local, "Microsoft", "Terminal Server Client", "Cache")),
                CleanerKind.DirectoryContents),
            // Electron 应用（含 VS Code 分支）都把 Chromium 缓存放在 userData 下的同构目录里，
            // 命中才显示；不触碰聊天记录、工作区与设置。
            new("signal-cache", Language.TargetsSignalCacheName, CleanupCategory.ApplicationData, CleanupRisk.Low,
                Language.TargetsSignalCacheDescription,
                "i-chat", Fixed(ChromiumCacheDirectories(environment.Under(roaming, "Signal"))),
                CleanerKind.DirectoryContents),
            new("whatsapp-cache", Language.TargetsWhatsappCacheName, CleanupCategory.ApplicationData, CleanupRisk.Low,
                Language.TargetsWhatsappCacheDescription,
                "i-chat", Fixed(ChromiumCacheDirectories(environment.Under(roaming, "WhatsApp"))),
                CleanerKind.DirectoryContents),
            new("notion-cache", Language.TargetsNotionCacheName, CleanupCategory.ApplicationData, CleanupRisk.Low,
                Language.TargetsNotionCacheDescription,
                "i-doc", Fixed(ChromiumCacheDirectories(environment.Under(roaming, "Notion"))),
                CleanerKind.DirectoryContents),
            new("postman-cache", Language.TargetsPostmanCacheName, CleanupCategory.ApplicationData, CleanupRisk.Low,
                Language.TargetsPostmanCacheDescription,
                "i-doc", Fixed(ChromiumCacheDirectories(environment.Under(roaming, "Postman"))),
                CleanerKind.DirectoryContents),
            new("figma-cache", Language.TargetsFigmaCacheName, CleanupCategory.ApplicationData, CleanupRisk.Low,
                Language.TargetsFigmaCacheDescription,
                "i-image", Fixed(ChromiumCacheDirectories(environment.Under(roaming, "Figma"))),
                CleanerKind.DirectoryContents),
            new("obsidian-cache", Language.TargetsObsidianCacheName, CleanupCategory.ApplicationData, CleanupRisk.Low,
                Language.TargetsObsidianCacheDescription,
                "i-doc", Fixed(ChromiumCacheDirectories(environment.Under(roaming, "obsidian"))),
                CleanerKind.DirectoryContents),
            new("cursor-cache", Language.TargetsCursorCacheName, CleanupCategory.PackageCache, CleanupRisk.Low,
                Language.TargetsCursorCacheDescription,
                "i-db", Fixed(ChromiumCacheDirectories(environment.Under(roaming, "Cursor"))),
                CleanerKind.DirectoryContents),

            // ── G6 Store 应用与 Game Bar ────────────────────────────────────────
            new("store-tempstate", Language.TargetsStoreTempstateName, CleanupCategory.GraphicsAndGameCache, CleanupRisk.Low,
                Language.TargetsStoreTempstateDescription,
                "i-folder", Fixed(environment.Package("*", "TempState")),
                CleanerKind.DirectoryContents, Tier: ScanTier.Slow, ScanTimeout: largeTreeTimeout,
                AllowedRoots: Fixed(environment.Under(local, "Packages"))),
            new("store-ac-temp", Language.TargetsStoreAcTempName, CleanupCategory.GraphicsAndGameCache, CleanupRisk.Low,
                Language.TargetsStoreAcTempDescription,
                "i-folder", Fixed(environment.Package("*", "AC", "Temp")),
                CleanerKind.DirectoryContents, Tier: ScanTier.Slow, ScanTimeout: largeTreeTimeout,
                AllowedRoots: Fixed(environment.Under(local, "Packages"))),
            new("store-localcache", Language.TargetsStoreLocalcacheName, CleanupCategory.GraphicsAndGameCache, CleanupRisk.Medium,
                Language.TargetsStoreLocalcacheDescription,
                "i-folder", Fixed(environment.Package("*", "LocalCache")),
                CleanerKind.DirectoryContents, Tier: ScanTier.Slow, ScanTimeout: largeTreeTimeout,
                AllowedRoots: Fixed(environment.Under(local, "Packages"))),

            // ── G8 开发缓存补充 ─────────────────────────────────────────────────
            new("huggingface-cache", Language.TargetsHuggingfaceCacheName, CleanupCategory.PackageCache, CleanupRisk.Medium,
                Language.TargetsHuggingfaceCacheDescription,
                "i-db", ToolPaths(toolchain, "huggingface"), CleanerKind.DirectoryContents,
                Tier: ScanTier.Slow, ScanTimeout: largeTreeTimeout),
            new("torch-cache", Language.TargetsTorchCacheName, CleanupCategory.PackageCache, CleanupRisk.Medium,
                Language.TargetsTorchCacheDescription,
                "i-db", ToolPaths(toolchain, "torch"), CleanerKind.DirectoryContents,
                Tier: ScanTier.Slow, ScanTimeout: largeTreeTimeout),
            new("conda-pkgs", Language.TargetsCondaPkgsName, CleanupCategory.PackageCache, CleanupRisk.Medium,
                Language.TargetsCondaPkgsDescription,
                "i-db", Multiple(Tool(toolchain, "conda"), ToolPath(toolchain, "anaconda", "pkgs")),
                CleanerKind.DirectoryContents, Tier: ScanTier.Slow, ScanTimeout: largeTreeTimeout),
            new("composer-cache", Language.TargetsComposerCacheName, CleanupCategory.PackageCache, CleanupRisk.Low,
                Language.TargetsComposerCacheDescription,
                "i-db", Multiple(Tool(toolchain, "composer"), Tool(toolchain, "composer-roaming")),
                CleanerKind.DirectoryContents),
            new("chocolatey-temp", Language.TargetsChocolateyTempName, CleanupCategory.PackageCache, CleanupRisk.Low,
                Language.TargetsChocolateyTempDescription,
                "i-db", Multiple(
                    Fixed(environment.Under(programData, "chocolatey", "logs")),
                    Fixed(environment.Under(programData, "chocolatey", "lib-bad")),
                    Fixed(environment.Under(temp, "chocolatey"))),
                CleanerKind.DirectoryContents, RequiresElevation: true),
            new("scoop-cache", Language.TargetsScoopCacheName, CleanupCategory.PackageCache, CleanupRisk.Low,
                Language.TargetsScoopCacheDescription,
                "i-db", ToolPath(toolchain, "scoop", "cache"), CleanerKind.DirectoryContents),
            new("winget-localstate", Language.TargetsWingetLocalstateName, CleanupCategory.PackageCache, CleanupRisk.Low,
                Language.TargetsWingetLocalstateDescription,
                "i-db", Fixed(environment.Package("Microsoft.DesktopAppInstaller_8wekyb3d8bbwe", "LocalState")),
                CleanerKind.DirectoryContents),
            new("vcpkg-downloads", Language.TargetsVcpkgDownloadsName, CleanupCategory.PackageCache, CleanupRisk.Medium,
                Language.TargetsVcpkgDownloadsDescription,
                "i-db", Multiple(
                    ToolPath(toolchain, "vcpkg", "downloads"),
                    ToolPath(toolchain, "vcpkg", "buildtrees")),
                CleanerKind.DirectoryContents, Tier: ScanTier.Slow, ScanTimeout: largeTreeTimeout),
            new("msys2-pacman-cache", Language.TargetsMsys2PacmanCacheName, CleanupCategory.PackageCache, CleanupRisk.Low,
                Language.TargetsMsys2PacmanCacheDescription,
                "i-db", ToolPath(toolchain, "msys2", "var", "cache", "pacman", "pkg"),
                CleanerKind.DirectoryContents, Tier: ScanTier.Slow),

            // ── G9 引擎与移动开发 ───────────────────────────────────────────────
            new("unity-cache", Language.TargetsUnityCacheName, CleanupCategory.PackageCache, CleanupRisk.Low,
                Language.TargetsUnityCacheDescription,
                "i-db", ToolPaths(toolchain, "unity"), CleanerKind.DirectoryContents,
                Tier: ScanTier.Slow, ScanTimeout: largeTreeTimeout),
            new("unreal-ddc", Language.TargetsUnrealDdcName, CleanupCategory.PackageCache, CleanupRisk.Low,
                Language.TargetsUnrealDdcDescription,
                "i-db", ToolPaths(toolchain, "unreal"), CleanerKind.DirectoryContents,
                Tier: ScanTier.Slow, ScanTimeout: largeTreeTimeout),
            new("android-sdk-temp", Language.TargetsAndroidSdkTempName, CleanupCategory.PackageCache, CleanupRisk.Low,
                Language.TargetsAndroidSdkTempDescription,
                "i-db", ToolPath(toolchain, "androidSdk", ".temp"), CleanerKind.DirectoryContents),
            new("android-avd", Language.TargetsAndroidAvdName, CleanupCategory.ApplicationData, CleanupRisk.Medium,
                Language.TargetsAndroidAvdDescription,
                "i-db", Fixed(environment.Under(profile, ".android", "avd")), Tier: ScanTier.Slow),
            new("rustup-toolchains", Language.TargetsRustupToolchainsName, CleanupCategory.ApplicationData, CleanupRisk.Medium,
                Language.TargetsRustupToolchainsDescription,
                "i-db", Fixed(environment.Under(profile, ".rustup", "toolchains")), Tier: ScanTier.Slow),

            // ── G13 开发缓存（保留 + 补齐） ─────────────────────────────────────
            new("nuget-global", Language.TargetsNugetGlobalName, CleanupCategory.PackageCache, CleanupRisk.Medium,
                Language.TargetsNugetGlobalDescription,
                "i-db", Fixed(nugetPackages), CleanerKind.Command,
                Command: new("dotnet", ["nuget", "locals", "global-packages", "--clear"],
                    AppText.Get(Language.CleanupCommandNuGetGlobal)),
                ScanTimeout: largeTreeTimeout,
                CheckLoadedModules: true, PathSource: _ => toolchain.ContainsKey("nuget") ? toolchain["nuget"].Source : AppText.Get(Language.RuntimeSourceDefault)),
            new("nuget-http", Language.TargetsNugetHttpName, CleanupCategory.PackageCache, CleanupRisk.Low,
                Language.TargetsNugetHttpDescription,
                "i-db", Fixed(environment.Under(local, "NuGet", "v3-cache")), CleanerKind.Command,
                Command: new("dotnet", ["nuget", "locals", "http-cache", "--clear"],
                    AppText.Get(Language.CleanupCommandNuGetHttp))),
            new("nuget-temp", Language.TargetsNugetTempName, CleanupCategory.TemporaryFiles, CleanupRisk.Low,
                Language.TargetsNugetTempDescription,
                "i-db", Fixed(environment.Under(temp, "NuGetScratch")), CleanerKind.Command,
                Command: new("dotnet", ["nuget", "locals", "temp", "--clear"],
                    AppText.Get(Language.CleanupCommandNuGetTemp))),
            new("nuget-plugins", Language.TargetsNugetPluginsName, CleanupCategory.PackageCache, CleanupRisk.Low,
                Language.TargetsNugetPluginsDescription,
                "i-db", Fixed(environment.Under(local, "NuGet", "plugins-cache")), CleanerKind.Command,
                Command: new("dotnet", ["nuget", "locals", "plugins-cache", "--clear"],
                    AppText.Get(Language.CleanupCommandNuGetPlugins))),
            new("user-temp", Language.TargetsUserTempName, CleanupCategory.TemporaryFiles, CleanupRisk.Low,
                Language.TargetsUserTempDescription,
                "i-file", Fixed(temp), CleanerKind.DirectoryContents, MinimumAge: oldFileCutoff),
            new("npm-cache", Language.TargetsNpmCacheName, CleanupCategory.PackageCache, CleanupRisk.Low,
                Language.TargetsNpmCacheDescription,
                "i-db", Fixed(npmCache), CleanerKind.Command,
                Command: new("npm", ["cache", "clean", "--force"], AppText.Get(Language.CleanupCommandNpm)),
                PathSource: _ => toolchain.TryGetValue("npm", out var resolution) ? resolution.Source : null),
            new("pnpm-store", Language.TargetsPnpmStoreName, CleanupCategory.PackageCache, CleanupRisk.Medium,
                Language.TargetsPnpmStoreDescription,
                "i-db", ToolPaths(toolchain, "pnpm"), CleanerKind.Command,
                Command: new("pnpm", ["store", "prune"], AppText.Get(Language.CleanupCommandPnpm)),
                PathSource: _ => toolchain.TryGetValue("pnpm", out var resolution) ? resolution.Source : null),
            new("yarn-cache", Language.TargetsYarnCacheName, CleanupCategory.PackageCache, CleanupRisk.Low,
                Language.TargetsYarnCacheDescription,
                "i-db", ToolPaths(toolchain, "yarn"), CleanerKind.Command,
                Command: new("yarn", ["cache", "clean"], AppText.Get(Language.CleanupCommandYarn)),
                PathSource: _ => toolchain.TryGetValue("yarn", out var resolution) ? resolution.Source : null),
            new("pip-cache", Language.TargetsPipCacheName, CleanupCategory.PackageCache, CleanupRisk.Low,
                Language.TargetsPipCacheDescription,
                "i-db", ToolPaths(toolchain, "pip"), CleanerKind.DirectoryContents,
                PathSource: _ => toolchain.TryGetValue("pip", out var resolution) ? resolution.Source : null),
            new("uv-cache", Language.TargetsUvCacheName, CleanupCategory.PackageCache, CleanupRisk.Low,
                Language.TargetsUvCacheDescription,
                "i-db", ToolPaths(toolchain, "uv"), CleanerKind.DirectoryContents),
            new("cargo-registry", Language.TargetsCargoRegistryName, CleanupCategory.PackageCache, CleanupRisk.Medium,
                Language.TargetsCargoRegistryDescription,
                "i-db", ToolPaths(toolchain, "cargo"), CleanerKind.DirectoryContents,
                Tier: ScanTier.Slow, ScanTimeout: largeTreeTimeout),
            new("go-modcache", Language.TargetsGoModcacheName, CleanupCategory.PackageCache, CleanupRisk.Medium,
                Language.TargetsGoModcacheDescription,
                "i-db", ToolPaths(toolchain, "go"), CleanerKind.DirectoryContents,
                Tier: ScanTier.Slow, ScanTimeout: largeTreeTimeout),
            new("gradle-caches", Language.TargetsGradleCachesName, CleanupCategory.PackageCache, CleanupRisk.Medium,
                Language.TargetsGradleCachesDescription,
                "i-db", ToolPaths(toolchain, "gradle"), CleanerKind.DirectoryContents,
                Tier: ScanTier.Slow, ScanTimeout: largeTreeTimeout),
            new("maven-repository", Language.TargetsMavenRepositoryName, CleanupCategory.PackageCache, CleanupRisk.Medium,
                Language.TargetsMavenRepositoryDescription,
                "i-db", ToolPaths(toolchain, "maven"), CleanerKind.DirectoryContents,
                Tier: ScanTier.Slow, ScanTimeout: largeTreeTimeout),
            new("vscode-cache", Language.TargetsVscodeCacheName, CleanupCategory.PackageCache, CleanupRisk.Low,
                Language.TargetsVscodeCacheDescription,
                "i-db", Multiple(
                    Fixed(environment.Under(roaming, "Code", "Cache")),
                    Fixed(environment.Under(roaming, "Code", "CachedData")),
                    Fixed(environment.Under(roaming, "Code", "logs"))),
                CleanerKind.DirectoryContents),
            new("jetbrains-caches", Language.TargetsJetbrainsCachesName, CleanupCategory.PackageCache, CleanupRisk.Medium,
                Language.TargetsJetbrainsCachesDescription,
                "i-db", Fixed(FindJetBrainsCaches(local)), CleanerKind.DirectoryContents,
                Tier: ScanTier.Slow, ScanTimeout: largeTreeTimeout, PathSource: _ => AppText.Get(Language.RuntimeSourceDirectory)),

            // ── G11 OEM / 驱动安装残留（仅分析） ────────────────────────────────
            new("oem-intel", Language.TargetsOemIntelName, CleanupCategory.SystemFiles, CleanupRisk.Medium,
                Language.TargetsOemIntelDescription,
                "i-chip", Fixed(environment.Under(drive, "Intel")), Tier: ScanTier.Slow),
            new("oem-amd", Language.TargetsOemAmdName, CleanupCategory.SystemFiles, CleanupRisk.Medium,
                Language.TargetsOemAmdDescription,
                "i-chip", Fixed(environment.Under(drive, "AMD")), Tier: ScanTier.Slow),
            new("oem-nvidia", Language.TargetsOemNvidiaName, CleanupCategory.SystemFiles, CleanupRisk.Medium,
                Language.TargetsOemNvidiaDescription,
                "i-chip", Fixed(environment.Under(drive, "NVIDIA")), Tier: ScanTier.Slow),
            new("oem-swsetup", Language.TargetsOemSwsetupName, CleanupCategory.SystemFiles, CleanupRisk.Medium,
                Language.TargetsOemSwsetupDescription,
                "i-window", Fixed(environment.Under(drive, "SWSetup")), Tier: ScanTier.Slow),
            new("oem-inetpub", Language.TargetsOemInetpubName, CleanupCategory.SystemFiles, CleanupRisk.Medium,
                Language.TargetsOemInetpubDescription,
                "i-doc", Fixed(environment.Under(drive, "inetpub", "logs")), Tier: ScanTier.Slow),

            // ── G12 系统分析档（有代价，仅分析） ────────────────────────────────
            new("winsxs", Language.TargetsWinsxsName, CleanupCategory.SystemFiles, CleanupRisk.High,
                Language.TargetsWinsxsDescription,
                "i-window", Fixed(environment.Windows32("WinSxS")),
                ScanKind: ScanKind.DismAnalyze, Tier: ScanTier.Slow, RequiresElevation: true,
                ScanNote: AppText.Get(Language.RuntimeAnalysisOnlyNote),
                ScanLabel: AppText.Get(Language.RuntimeDismLabel)),
            new("windows-installer", Language.TargetsWindowsInstallerName, CleanupCategory.SystemFiles, CleanupRisk.High,
                Language.TargetsWindowsInstallerDescription,
                "i-db", Fixed(environment.Windows32("Installer")), Tier: ScanTier.Slow),
            new("package-cache", Language.TargetsPackageCacheName, CleanupCategory.SystemFiles, CleanupRisk.Medium,
                Language.TargetsPackageCacheDescription,
                "i-db", Fixed(environment.Under(programData, "Package Cache")), Tier: ScanTier.Slow),
            new("search-index", Language.TargetsSearchIndexName, CleanupCategory.SystemFiles, CleanupRisk.Medium,
                Language.TargetsSearchIndexDescription,
                "i-db", Fixed(environment.Under(programData, "Microsoft", "Search", "Data")), Tier: ScanTier.Slow),
            new("font-cache", Language.TargetsFontCacheName, CleanupCategory.SystemFiles, CleanupRisk.Medium,
                Language.TargetsFontCacheDescription,
                "i-doc", Fixed(environment.Under(windows, "ServiceProfiles", "LocalService", "AppData", "Local", "FontCache")),
                Tier: ScanTier.Slow, RequiresElevation: true),
            new("vss-shadow", Language.TargetsVssShadowName, CleanupCategory.SystemFiles, CleanupRisk.High,
                Language.TargetsVssShadowDescription,
                "i-clock", Empty, ScanKind: ScanKind.VssQuery, Tier: ScanTier.Slow, RequiresElevation: true,
                LocationOverride: "vssadmin list shadowstorage", ScanNote: AppText.Get(Language.RuntimeAnalysisOnlyNote),
                ScanLabel: AppText.Get(Language.RuntimeVssLabel)),
            new("cbs-logs", Language.TargetsCbsLogsName, CleanupCategory.SystemLogs, CleanupRisk.Medium,
                Language.TargetsCbsLogsDescription,
                "i-doc", Fixed(environment.Windows32(@"Logs\CBS")), Tier: ScanTier.Slow, RequiresElevation: true),
            new("etl-logs", Language.TargetsEtlLogsName, CleanupCategory.SystemLogs, CleanupRisk.Medium,
                Language.TargetsEtlLogsDescription,
                "i-doc", Fixed(environment.Windows32(@"System32\LogFiles")), IncludePatterns: ["*.etl"],
                ScanKind: ScanKind.FilePattern, Tier: ScanTier.Slow, RequiresElevation: true),
            new("wsl-docker-vhdx", Language.TargetsWslDockerVhdxName, CleanupCategory.SystemFiles, CleanupRisk.Medium,
                Language.TargetsWslDockerVhdxDescription,
                "i-db", Multiple(
                    environment.Package("*", "LocalState", "ext4.vhdx"),
                    environment.Under(local, "Docker", "wsl", "*", "data", "ext4.vhdx")),
                ScanKind: ScanKind.SingleFile, Tier: ScanTier.Slow),
            new("swapfile", Language.TargetsSwapfileName, CleanupCategory.SystemFiles, CleanupRisk.Medium,
                Language.TargetsSwapfileDescription,
                "i-db", Fixed(environment.Under(drive, "swapfile.sys")), RequiresElevation: true),
            new("windows-old", Language.TargetsWindowsOldName, CleanupCategory.SystemFiles, CleanupRisk.High,
                Language.TargetsWindowsOldDescription,
                "i-window", Fixed(environment.Under(drive, "Windows.old")),
                Tier: ScanTier.Slow, RequiresElevation: true, IsProtected: true),
            new("hiberfil", Language.TargetsHiberfilName, CleanupCategory.SystemFiles, CleanupRisk.Medium,
                Language.TargetsHiberfilDescription,
                "i-moon", Fixed(environment.Under(drive, "hiberfil.sys")),
                RequiresElevation: true, IsProtected: true),
            new("pagefile", Language.TargetsPagefileName, CleanupCategory.SystemFiles, CleanupRisk.Medium,
                Language.TargetsPagefileDescription,
                "i-db", Fixed(environment.Under(drive, "pagefile.sys")),
                RequiresElevation: true, IsProtected: true),
            new("memory-dump", Language.TargetsMemoryDumpName, CleanupCategory.SystemFiles, CleanupRisk.Medium,
                Language.TargetsMemoryDumpDescription,
                "i-chip", Fixed(environment.Windows32("MEMORY.DMP")),
                RequiresElevation: true, IsProtected: true),
            new("codex-data", Language.TargetsCodexDataName, CleanupCategory.ApplicationData, CleanupRisk.High,
                Language.TargetsCodexDataDescription,
                "i-chat", Multiple(
                    environment.Under(profile, ".codex", "sessions"),
                    environment.Under(profile, ".codex", "archived_sessions")),
                CleanerKind.CodexConversations, LocationOverride: environment.Under(profile, ".codex")),
            new("recycle-bin", Language.RuntimeRecycleBin, CleanupCategory.RecycleBin, CleanupRisk.Medium,
                Language.RuntimeRecycleWarning,
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
