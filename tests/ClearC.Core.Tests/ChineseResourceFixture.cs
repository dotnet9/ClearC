using System.Globalization;
using CodeWF.Toolkit.Lang;

namespace ClearC.Core.Tests;

/// <summary>明确请求中文资源，避免本机语言决定格式化测试结果。</summary>
[CollectionDefinition(nameof(ChineseResourceCollection), DisableParallelization = true)]
public sealed class ChineseResourceCollection : ICollectionFixture<ChineseResourceFixture>
{
}

public sealed class ChineseResourceFixture : IDisposable
{
    private readonly CultureInfo? _previousCulture = I18nManager.Instance.Culture;

    public ChineseResourceFixture() => I18nManager.Instance.Culture = CultureInfo.GetCultureInfo("zh-CN");

    public void Dispose() => I18nManager.Instance.Culture = _previousCulture;
}
