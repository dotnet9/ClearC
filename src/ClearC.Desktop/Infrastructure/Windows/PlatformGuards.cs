namespace ClearC.Desktop.Infrastructure.Windows;

/// <summary>
/// 平台判定集中在这里，便于非 Windows 平台上做守卫与单元测试。
/// </summary>
internal interface IPlatform
{
    bool IsWindows { get; }
}

internal sealed class SystemPlatform : IPlatform
{
    public static SystemPlatform Instance { get; } = new();

    public bool IsWindows => OperatingSystem.IsWindows();
}
