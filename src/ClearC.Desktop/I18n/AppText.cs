namespace ClearC.Desktop.I18n;

/// <summary>ViewModel 使用的国际化文本便捷入口。</summary>
internal static class AppText
{
    public static string Get(string key) => AppLocalizer.Instance.Get(key);

    public static string Format(string key, params object?[] args) => AppLocalizer.Instance.Format(key, args);
}
