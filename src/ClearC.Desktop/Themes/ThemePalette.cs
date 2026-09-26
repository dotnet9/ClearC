using Avalonia;
using Avalonia.Media;
using ClearC.Core.Models;

namespace ClearC.Desktop.Themes;

/// <summary>
/// 视图模型取色的唯一入口：所有颜色都从 <c>ClearCPalette.axaml</c> 的资源里取，
/// 不在 C# 里重复写死色值。取不到（例如没有 Avalonia 应用上下文）时返回白色占位，不影响逻辑断言。
/// </summary>
public interface IThemePalette
{
    IBrush Brush(string resourceKey);

    IBrush RiskForeground(CleanupRisk risk);

    IBrush RiskBackground(CleanupRisk risk);

    IBrush LogLevelForeground(string level);
}

public sealed class ThemePalette : IThemePalette
{
    public static ThemePalette Instance { get; } = new();

    private ThemePalette()
    {
    }

    public IBrush Brush(string resourceKey) =>
        Application.Current is { } application &&
        application.TryGetResource(resourceKey, application.ActualThemeVariant, out var value) &&
        value is IBrush brush
            ? brush
            : Brushes.Transparent;

    public IBrush RiskForeground(CleanupRisk risk) => Brush(risk switch
    {
        CleanupRisk.Low => "ClearCRiskLowText",
        CleanupRisk.Medium => "ClearCRiskMidText",
        _ => "ClearCRiskHighText"
    });

    public IBrush RiskBackground(CleanupRisk risk) => Brush(risk switch
    {
        CleanupRisk.Low => "ClearCRiskLowBg",
        CleanupRisk.Medium => "ClearCRiskMidBg",
        _ => "ClearCRiskHighBg"
    });

    public IBrush LogLevelForeground(string level) => Brush(level switch
    {
        "OK" => "ClearCLogOk",
        "WARN" => "ClearCLogWarn",
        "ERR" => "ClearCLogErr",
        _ => "ClearCLogInfo"
    });
}
