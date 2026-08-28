using System.ComponentModel;
using ReactiveUI;

namespace ClearC.Desktop.ViewModels;

public abstract class MainWindowSectionViewModel : ReactiveObject
{
    private readonly HashSet<string> _forwardedProperties;

    protected MainWindowSectionViewModel(MainWindowViewModel owner, params string[] forwardedProperties)
    {
        Owner = owner;
        _forwardedProperties = forwardedProperties.ToHashSet(StringComparer.Ordinal);
        Owner.PropertyChanged += OnOwnerPropertyChanged;
    }

    protected MainWindowViewModel Owner { get; }

    private void OnOwnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null)
        {
            foreach (var propertyName in _forwardedProperties)
            {
                this.RaisePropertyChanged(propertyName);
            }

            return;
        }

        if (_forwardedProperties.Contains(e.PropertyName))
        {
            this.RaisePropertyChanged(e.PropertyName);
        }
    }
}
