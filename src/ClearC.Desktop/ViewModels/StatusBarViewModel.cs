using Avalonia.Media;

namespace ClearC.Desktop.ViewModels;

public sealed class StatusBarViewModel : MainWindowSectionViewModel
{
    public StatusBarViewModel(MainWindowViewModel owner)
        : base(owner, nameof(StatusText), nameof(StatusBrush), nameof(StateCode))
    {
    }

    public string StatusText => Owner.StatusText;
    public IBrush StatusBrush => Owner.StatusBrush;
    public string StateCode => Owner.StateCode;
}
