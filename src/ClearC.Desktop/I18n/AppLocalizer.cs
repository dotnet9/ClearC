using System.Globalization;
using Avalonia;
using Avalonia.Threading;
using CodeWF.Toolkit.Lang;
using CodeWF.Toolkit.Lang.Json;

namespace ClearC.Desktop.I18n;

/// <summary>ClearC 应用国际化入口，资源由宿主输出目录 JSON 提供。</summary>
public sealed class AppLocalizer
{
    public static AppLocalizer Instance { get; } = new();
    private static bool _initialized;

    private AppLocalizer()
    {
    }

    /// <summary>当前界面语言发生变化时触发。</summary>
    public event EventHandler<EventArgs>? CultureChanged
    {
        add => I18nManager.Instance.CultureChanged += value;
        remove => I18nManager.Instance.CultureChanged -= value;
    }

    public CultureInfo Culture => I18nManager.Instance.Culture ?? CultureInfo.GetCultureInfo("zh-CN");

    public void SetCulture(string cultureName)
    {
        var culture = IsSupported(cultureName) ? CultureInfo.GetCultureInfo(cultureName) : CultureInfo.GetCultureInfo("zh-CN");
        I18nManager.Instance.Culture = culture;
    }

    public string Get(string key)
    {
        EnsureInitialized();
        return I18nManager.Instance.GetResource(key);
    }

    public string Format(string key, params object?[] args) => string.Format(Culture, Get(key), args);

    public static void Initialize(string cultureName)
    {
        if (_initialized)
        {
            Instance.SetCulture(cultureName);
            return;
        }

        var culture = IsSupported(cultureName) ? CultureInfo.GetCultureInfo(cultureName) : CultureInfo.GetCultureInfo("zh-CN");
        // macOS 标准应用包把宿主资源放在 Contents/Resources。
        var bundledResources = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Resources", "I18n"));
        var plugin = new JsonLangPlugin
        {
            ResourceFolder = OperatingSystem.IsMacOS() && Directory.Exists(bundledResources)
                ? bundledResources : Path.Combine(AppContext.BaseDirectory, "I18n")
        };
        if (!I18nManager.Instance.Register(plugin, culture, out var error))
        {
            throw new InvalidOperationException(error ?? "Unable to load application language resources.");
        }

        I18nManager.Instance.CultureChanged += OnCultureChanged;
        ApplyThirdPartyCulture();
        _initialized = true;
    }

    private static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        try
        {
            Initialize("zh-CN");
        }
        catch (InvalidOperationException)
        {
            // 纯核心测试或设计时加载没有输出资源时，保留资源键作为最后兜底。
        }
    }

    private static void OnCultureChanged(object? sender, EventArgs args) => ApplyThirdPartyCulture();

    /// <summary>更新主题自身的内建资源，保留宿主与局部资源的同键覆盖。</summary>
    private static void ApplyThirdPartyCulture()
    {
        if (Application.Current is not { PlatformSettings: not null } application) return;
        // 目录扫描可在线程池初始化文本资源，第三方主题必须在界面线程更新。
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ApplyThirdPartyCulture);
            return;
        }
        var culture = Instance.Culture;
        foreach (var theme in application.Styles.OfType<Semi.Avalonia.SemiTheme>()) theme.Locale = culture;
        foreach (var theme in application.Styles.OfType<Ursa.Themes.Semi.UrsaSemiTheme>()) theme.Locale = culture;
    }

    private static bool IsSupported(string? value) => value is "zh-CN" or "en-US";

}
