using Avalonia.Media;

namespace ClearC.Desktop.ViewModels;

public sealed class StatusBarViewModel : MainWindowSectionViewModel
{
    public StatusBarViewModel(MainWindowViewModel owner)
        : base(owner, nameof(StatusText), nameof(StatusBrush), nameof(IsStatusPulsing), nameof(StateCode))
    {
    }

    public string StatusText => Owner.StatusText;
    public IBrush StatusBrush => Owner.StatusBrush;
    public bool IsStatusPulsing => Owner.IsStatusPulsing;
    public string StateCode => Owner.StateCode;
}
