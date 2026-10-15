using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ClearC.Desktop.I18n;
using ClearC.Desktop.Services;
using CodeWF.Avalonia.Controls.Themes;
using ReactiveUI;

namespace ClearC.Desktop.ViewModels;

/// <summary>外观选择与配置落盘；配色及系统主题变化由共享控件库处理。</summary>
public sealed class AppearanceViewModel : ReactiveObject, IDisposable
{
    private readonly StudioThemeController _controller;
    private readonly AppearanceSettingsStore _store;
    private readonly Action<Exception>? _onSaveError;

    public AppearanceViewModel(StudioThemeController controller, AppearanceSettingsStore store,
        Action<Exception>? onSaveError = null)
    {
        _controller = controller;
        _store = store;
        _onSaveError = onSaveError;
        var settings = store.Load();
        _controller.Mode = settings.Mode;
        _controller.Palette = settings.Palette;
        _controller.PropertyChanged += OnThemeChanged;
        AppLocalizer.Instance.CultureChanged += OnCultureChanged;
    }

    public IReadOnlyList<AppearanceOption<StudioThemeMode>> Modes { get; } =
    [
        new(StudioThemeMode.System, Language.AppearanceSystem),
        new(StudioThemeMode.Light, Language.AppearanceLight),
        new(StudioThemeMode.Dark, Language.AppearanceDark)
    ];

    public IReadOnlyList<PaletteChoice> Palettes { get; } =
    [
        new(StudioPalette.DotNet, Language.AppearanceDotNet),
        new(StudioPalette.Aurora, Language.AppearanceAurora),
        new(StudioPalette.Ember, Language.AppearanceEmber),
        new(StudioPalette.Default, Language.AppearanceDefault),
        new(StudioPalette.Aquatic, Language.AppearanceAquatic),
        new(StudioPalette.Desert, Language.AppearanceDesert),
        new(StudioPalette.Dusk, Language.AppearanceDusk),
        new(StudioPalette.NightSky, Language.AppearanceNightSky)
    ];

    public IReadOnlyList<AppearanceOption<string>> Languages { get; } =
    [
        new("zh-CN", Language.AppearanceChinese),
        new("en-US", Language.AppearanceEnglish)
    ];

    public StudioThemeMode Mode { get => _controller.Mode; set => _controller.Mode = value; }
    public StudioPalette Palette { get => _controller.Palette; set => _controller.Palette = value; }
    public string CultureName { get => AppLocalizer.Instance.Culture.Name; set => AppLocalizer.Instance.SetCulture(value); }

    private void OnThemeChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs args)
    {
        this.RaisePropertyChanged(nameof(Mode));
        this.RaisePropertyChanged(nameof(Palette));
        Save();
    }

    private void OnCultureChanged(object? sender, EventArgs args)
    {
        foreach (var option in Modes) option.Refresh();
        foreach (var option in Palettes) option.Refresh();
        foreach (var option in Languages) option.Refresh();
        this.RaisePropertyChanged(nameof(CultureName));
        Save();
    }

    private void Save()
    {
        try { _store.Save(new(Mode, Palette, CultureName)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { _onSaveError?.Invoke(error); }
    }

    public void Dispose()
    {
        _controller.PropertyChanged -= OnThemeChanged;
        AppLocalizer.Instance.CultureChanged -= OnCultureChanged;
        _controller.Dispose();
    }
}

/// <summary>选择项保持实例稳定，切换语言仅刷新显示文本。</summary>
public class AppearanceOption<T>(T value, string resourceKey) : ReactiveObject
{
    public T Value { get; } = value;
    public string DisplayName => AppText.Get(resourceKey);
    internal void Refresh() => this.RaisePropertyChanged(nameof(DisplayName));
}

/// <summary>配色预览从共享主题字典读取，不在业务代码中重复维护颜色。</summary>
public sealed class PaletteChoice(StudioPalette value, string resourceKey) : AppearanceOption<StudioPalette>(value, resourceKey)
{
    public IBrush PreviewBrush { get; } = Application.Current is { } application &&
        application.TryGetResource("StudioAccentColor", StudioTheme.GetVariant(value, false), out var color) && color is Color resolved
            ? new SolidColorBrush(resolved)
            : Brushes.Transparent;
}
