using ClearC.Desktop.I18n;
using ReactiveUI;

namespace ClearC.Desktop.ViewModels;

public sealed class TitleBarViewModel : ReactiveObject
{
    public string ProductName => "CLEARC";
    public string Tagline => AppText.Get(Language.Tagline);

    public TitleBarViewModel() => AppLocalizer.Instance.CultureChanged += OnCultureChanged;

    private void OnCultureChanged(object? sender, EventArgs args) => this.RaisePropertyChanged(nameof(Tagline));
}
