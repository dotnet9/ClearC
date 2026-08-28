using ClearC.Core.Models;
using ReactiveUI;

namespace ClearC.Desktop.ViewModels;

public sealed class CategoryFilterViewModel(CleanupCategory? category, string label) : ReactiveObject
{
    private bool _isActive;
    private int _count;

    public CleanupCategory? Category { get; } = category;

    public string Label { get; } = label;

    public int Count
    {
        get => _count;
        set => this.RaiseAndSetIfChanged(ref _count, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set => this.RaiseAndSetIfChanged(ref _isActive, value);
    }
}
