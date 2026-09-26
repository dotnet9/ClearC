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
            nameof(IsConfirmationWarningVisible),
            nameof(ConfirmationPendingNote),
            nameof(IsPendingNoteVisible),
            nameof(IsConfirmEnabled),
            nameof(HasDeniedItems),
            nameof(IsToastVisible),
            nameof(IsToastSuccess),
            nameof(ToastText),
            nameof(IsCloseConfirmationVisible),
            nameof(CloseConfirmationText))
    {
    }

    public ObservableCollection<CleanupItemViewModel> SelectedItems => Owner.SelectedItems;
    public bool IsConfirmationVisible => Owner.IsConfirmationVisible;
    public string ConfirmationTotal => Owner.ConfirmationTotal;
    public string ConfirmationWarning => Owner.ConfirmationWarning;
    public bool IsConfirmationWarningVisible => Owner.IsConfirmationWarningVisible;
    public string ConfirmationPendingNote => Owner.ConfirmationPendingNote;
    public bool IsPendingNoteVisible => Owner.IsPendingNoteVisible;
    public bool IsConfirmEnabled => Owner.IsConfirmEnabled;
    public bool HasDeniedItems => Owner.HasDeniedItems;
    public ICommand CancelConfirmationCommand => Owner.CancelConfirmationCommand;
    public ICommand ConfirmCleanupCommand => Owner.ConfirmCleanupCommand;
    public ICommand ClearDeniedCommand => Owner.ClearDeniedCommand;
    public bool IsToastVisible => Owner.IsToastVisible;
    public bool IsToastSuccess => Owner.IsToastSuccess;
    public string ToastText => Owner.ToastText;
    public bool IsCloseConfirmationVisible => Owner.IsCloseConfirmationVisible;
    public string CloseConfirmationText => Owner.CloseConfirmationText;
    public ICommand CancelCloseCommand => Owner.CancelCloseCommand;
    public ICommand ConfirmCloseCommand => Owner.ConfirmCloseCommand;
}
