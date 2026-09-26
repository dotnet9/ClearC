using System.Collections.ObjectModel;
using ClearC.Core.Formatting;
using ClearC.Core.Models;
using ReactiveUI;

namespace ClearC.Desktop.ViewModels;

/// <summary>结果列表里的一个展示分组（原型 <c>.group</c>）：吸顶组头 + 行集合。</summary>
public sealed class CleanupGroupViewModel : ReactiveObject
{
    private bool _isExpanded = true;
    private long _subtotalBytes;

    public CleanupGroupViewModel(CleanupDisplayGroup group)
    {
        Group = group;
        Name = group.ToDisplayName();
    }

    public CleanupDisplayGroup Group { get; }
    public string Name { get; }
    public ObservableCollection<CleanupItemViewModel> Items { get; } = [];

    public int Count => Items.Count;
    public string CountText => $"{Count} 项";

    public long SubtotalBytes
    {
        get => _subtotalBytes;
        private set
        {
            if (_subtotalBytes != value)
            {
                this.RaiseAndSetIfChanged(ref _subtotalBytes, value);
                this.RaisePropertyChanged(nameof(SubtotalText));
            }
        }
    }

    public string SubtotalText => ByteSizeFormatter.Format(SubtotalBytes);

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                this.RaiseAndSetIfChanged(ref _isExpanded, value);
                this.RaisePropertyChanged(nameof(ChevronRotation));
            }
        }
    }

    /// <summary>折叠时 chevron 旋转 -90°。</summary>
    public double ChevronRotation => IsExpanded ? 0 : -90;

    internal void Refresh(IReadOnlyList<CleanupItemViewModel> rows)
    {
        Items.Clear();
        foreach (var row in rows)
        {
            Items.Add(row);
        }

        SubtotalBytes = rows.Sum(row => row.Model.SizeBytes);
        this.RaisePropertyChanged(nameof(Count));
        this.RaisePropertyChanged(nameof(CountText));
    }
}
