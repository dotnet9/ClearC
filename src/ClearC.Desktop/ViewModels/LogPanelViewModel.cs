using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using ClearC.Desktop.Infrastructure.Logging;
using ClearC.Desktop.Themes;
using ReactiveUI;

namespace ClearC.Desktop.ViewModels;

public sealed class LogEntryViewModel
{
    public LogEntryViewModel(LogEntry entry, IThemePalette palette)
    {
        Timestamp = entry.Timestamp.ToString("HH:mm:ss");
        Level = entry.Level;
        Message = entry.Message;
        LevelBrush = palette.LogLevelForeground(entry.Level);
    }

    public string Timestamp { get; }
    public string Level { get; }
    public string Message { get; }
    public IBrush LevelBrush { get; }
}

public sealed class LogPanelViewModel : MainWindowSectionViewModel
{
    private readonly IThemePalette _palette;

    public LogPanelViewModel(MainWindowViewModel owner, InMemoryLogStore store, IThemePalette? palette = null)
        : base(owner, nameof(LogCount), nameof(LogCountText))
    {
        _palette = palette ?? ThemePalette.Instance;
        Store = store;
        Entries = [];
        store.Entries.CollectionChanged += (_, _) => Refresh();
        Refresh();
    }

    public InMemoryLogStore Store { get; }

    public ObservableCollection<LogEntryViewModel> Entries { get; }
    public int LogCount => Entries.Count;
    public string LogCountText => LogCount.ToString("N0");
    public string LogText => Store.ToText();
    public ICommand ClearLogCommand => Owner.ClearLogCommand;

    private void Refresh()
    {
        void Apply()
        {
            Entries.Clear();
            foreach (var entry in Store.Entries)
            {
                Entries.Add(new LogEntryViewModel(entry, _palette));
            }

            this.RaisePropertyChanged(nameof(LogCount));
            this.RaisePropertyChanged(nameof(LogCountText));
        }

        if (Application.Current is null || Dispatcher.UIThread.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.UIThread.Post(Apply);
        }
    }
}
