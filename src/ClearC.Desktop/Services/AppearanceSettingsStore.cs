using System.Text.Json;
using System.Text.Json.Serialization;
using CodeWF.Avalonia.Controls.Themes;

namespace ClearC.Desktop.Services;

/// <summary>应用外观配置使用源生成 JSON 存储，支持 NativeAOT。</summary>
public sealed class AppearanceSettingsStore(string? filePath = null)
{
    private readonly string _filePath = filePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClearC", "appearance.json");

    public AppearanceSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return new();
            var settings = JsonSerializer.Deserialize(File.ReadAllText(_filePath), AppearanceJsonContext.Default.AppearanceSettings);
            return settings is not null && Enum.IsDefined(settings.Mode) && Enum.IsDefined(settings.Palette) ? settings : new();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    public void Save(AppearanceSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_filePath))!);
        var temporary = _filePath + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, AppearanceJsonContext.Default.AppearanceSettings));
            File.Move(temporary, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

public sealed record AppearanceSettings(
    StudioThemeMode Mode = StudioThemeMode.System,
    StudioPalette Palette = StudioPalette.DotNet,
    string? CultureName = null);

[JsonSerializable(typeof(AppearanceSettings))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
internal partial class AppearanceJsonContext : JsonSerializerContext;
