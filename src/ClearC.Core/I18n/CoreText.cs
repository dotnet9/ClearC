using CodeWF.Toolkit.Lang.Json;

namespace ClearC.Core.I18n;

/// <summary>核心程序集内嵌资源支持独立使用，并自动接受宿主同键覆盖。</summary>
internal static class CoreText
{
    private static readonly JsonLocalizer Resources = new(typeof(CoreText).Assembly, "zh-CN");
    internal static string Get(string key) => Resources.GetResource(key);
    internal static string Format(string key, params object?[] args) => Resources.Format(key, args);
}
