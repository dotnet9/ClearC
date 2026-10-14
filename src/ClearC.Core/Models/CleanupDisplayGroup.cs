using ClearC.Core.I18n;

namespace ClearC.Core.Models;

/// <summary>界面上的 6 个固定展示分组（原型 <c>CATS.slice(1)</c>）。</summary>
public enum CleanupDisplayGroup
{
    TemporaryFiles,
    SystemCache,
    RecycleBin,
    Browser,
    UserFiles,
    SystemFiles
}

public static class CleanupDisplayGroupMap
{
    public static CleanupDisplayGroup ToDisplayGroup(this CleanupCategory category) => category switch
    {
        CleanupCategory.TemporaryFiles => CleanupDisplayGroup.TemporaryFiles,
        CleanupCategory.RecycleBin => CleanupDisplayGroup.RecycleBin,
        CleanupCategory.BrowserCache => CleanupDisplayGroup.Browser,
        CleanupCategory.ApplicationData => CleanupDisplayGroup.UserFiles,
        CleanupCategory.SystemFiles => CleanupDisplayGroup.SystemFiles,
        _ => CleanupDisplayGroup.SystemCache
    };

    public static string ToDisplayName(this CleanupDisplayGroup group) => group switch
    {
        CleanupDisplayGroup.TemporaryFiles => CoreText.Get(CoreLanguage.CoreGroupTemporaryFiles),
        CleanupDisplayGroup.SystemCache => CoreText.Get(CoreLanguage.CoreGroupSystemCache),
        CleanupDisplayGroup.RecycleBin => CoreText.Get(CoreLanguage.CoreGroupRecycleBin),
        CleanupDisplayGroup.Browser => CoreText.Get(CoreLanguage.CoreGroupBrowser),
        CleanupDisplayGroup.UserFiles => CoreText.Get(CoreLanguage.CoreGroupUserFiles),
        _ => CoreText.Get(CoreLanguage.CoreGroupSystemFiles)
    };
}
