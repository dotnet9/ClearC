using System.Collections.ObjectModel;
using System.Windows.Input;

namespace ClearC.Desktop.ViewModels;

public sealed class LogPanelViewModel : MainWindowSectionViewModel
{
    public LogPanelViewModel(MainWindowViewModel owner)
        : base(owner, nameof(LogCount))
    {
    }

    public ObservableCollection<LogEntryViewModel> Logs => Owner.Logs;
    public int LogCount => Owner.LogCount;
    public ICommand ClearLogsCommand => Owner.ClearLogsCommand;
}
