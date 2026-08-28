using ClearC.Core.Models;

namespace ClearC.Core.Selection;

public sealed class CleanupSelection
{
    private readonly IReadOnlyList<CleanupItem> _items;
    private readonly HashSet<string> _selectedIds;

    public CleanupSelection(IEnumerable<CleanupItem> items)
    {
        _items = items?.ToArray() ?? throw new ArgumentNullException(nameof(items));
        _selectedIds = _items.Where(item => item.IsRecommended).Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
    }

    public IReadOnlySet<string> SelectedIds => _selectedIds;

    public int SelectedCount => _items.Count(item => _selectedIds.Contains(item.Id));

    public long SelectedBytes => _items.Where(item => _selectedIds.Contains(item.Id)).Sum(item => item.SizeBytes);

    public bool IsSelected(string id) => _selectedIds.Contains(id);

    public void SetSelected(string id, bool selected)
    {
        var item = _items.FirstOrDefault(candidate => candidate.Id == id)
            ?? throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown cleanup item.");

        if (selected && item.CanClean)
        {
            _selectedIds.Add(id);
        }
        else
        {
            _selectedIds.Remove(id);
        }
    }

    public void SetAll(bool selected, CleanupCategory? category = null)
    {
        foreach (var item in _items.Where(item => category is null || item.Category == category))
        {
            SetSelected(item.Id, selected);
        }
    }
}
