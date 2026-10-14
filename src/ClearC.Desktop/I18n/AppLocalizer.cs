using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CodeWF.Avalonia.Lang;
using CodeWF.Avalonia.Lang.Json;

namespace ClearC.Desktop.I18n;

/// <summary>ClearC 应用国际化入口，资源由宿主输出目录 JSON 提供。</summary>
public sealed class AppLocalizer
{
    private static readonly IReadOnlyDictionary<string, Func<ResourceDictionary>> SemiCultures =
        new Dictionary<string, Func<ResourceDictionary>>(StringComparer.OrdinalIgnoreCase)
        {
            ["en-US"] = () => new Semi.Avalonia.Locale.en_us(),
            ["zh-CN"] = () => new Semi.Avalonia.Locale.zh_cn()
        };

    private static readonly IReadOnlyDictionary<string, Func<ResourceDictionary>> UrsaCultures =
        new Dictionary<string, Func<ResourceDictionary>>(StringComparer.OrdinalIgnoreCase)
        {
            ["en-US"] = () => new Ursa.Themes.Semi.Locale.en_us(),
            ["zh-CN"] = () => new Ursa.Themes.Semi.Locale.zh_cn()
        };

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
        ApplyThirdPartyCulture(culture.Name);
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
        var plugin = new JsonLangPlugin { ResourceFolder = Path.Combine(AppContext.BaseDirectory, "I18n") };
        if (!I18nManager.Instance.Register(plugin, culture, out var error))
        {
            throw new InvalidOperationException(error ?? "Unable to load application language resources.");
        }

        ApplyThirdPartyCulture(culture.Name);
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

    public static void ApplyThirdPartyCulture(string cultureName)
    {
        if (Application.Current is not { PlatformSettings: not null } application) return;
        // 目录扫描可在线程池初始化文本资源，第三方 XAML 资源必须在界面线程创建和合并。
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ApplyThirdPartyCulture(cultureName));
            return;
        }
        if (SemiCultures.TryGetValue(cultureName, out var semi)) MergeCultureResources(application.Resources, semi());
        if (UrsaCultures.TryGetValue(cultureName, out var ursa)) MergeCultureResources(application.Resources, ursa());
    }

    private static bool IsSupported(string? value) => value is "zh-CN" or "en-US";

    private static void MergeCultureResources(IResourceDictionary target, ResourceDictionary culture)
    {
        foreach (var item in culture)
        {
            if (target.ContainsKey(item.Key)) target.Remove(item.Key);
            target.Add(item);
        }
    }
}
