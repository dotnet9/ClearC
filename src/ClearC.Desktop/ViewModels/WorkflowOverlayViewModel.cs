using System.Collections.ObjectModel;
using System.Windows.Input;

namespace ClearC.Desktop.ViewModels;

public sealed class WorkflowOverlayViewModel : MainWindowSectionViewModel
{
    public WorkflowOverlayViewModel(MainWindowViewModel owner)
        : base(
            owner,
            nameof(IsConfirmationVisible),
            nameof(ConfirmationTotal),
            nameof(ConfirmationWarning),
            nameof(IsToastVisible),
            nameof(ToastText))
    {
    }

    public ObservableCollection<CleanupItemViewModel> SelectedItems => Owner.SelectedItems;
    public bool IsConfirmationVisible => Owner.IsConfirmationVisible;
    public string ConfirmationTotal => Owner.ConfirmationTotal;
    public string ConfirmationWarning => Owner.ConfirmationWarning;
    public ICommand CancelConfirmationCommand => Owner.CancelConfirmationCommand;
    public ICommand ConfirmCleanupCommand => Owner.ConfirmCleanupCommand;
    public bool IsToastVisible => Owner.IsToastVisible;
    public string ToastText => Owner.ToastText;
}
