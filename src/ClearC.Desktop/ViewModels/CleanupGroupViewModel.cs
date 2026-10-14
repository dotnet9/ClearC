using System.Collections.ObjectModel;
using ClearC.Core.Formatting;
using ClearC.Core.Models;
using ClearC.Desktop.I18n;
using ReactiveUI;

namespace ClearC.Desktop.ViewModels;

/// <summary>结果列表里的一个展示分组（原型 <c>.group</c>）：吸顶组头 + 行集合。</summary>
public sealed class CleanupGroupViewModel : ReactiveObject
{
    private readonly Action<CleanupGroupViewModel>? _onExpanded;
    private bool _isExpanded;
    private long _subtotalBytes;

    public CleanupGroupViewModel(CleanupDisplayGroup group, Action<CleanupGroupViewModel>? onExpanded = null)
    {
        Group = group;
        _onExpanded = onExpanded;
        AppLocalizer.Instance.CultureChanged += OnCultureChanged;
    }

    public CleanupDisplayGroup Group { get; }
    public string Name => LocalizedName(Group);
    public ObservableCollection<CleanupItemViewModel> Items { get; } = [];

    public int Count => Items.Count;
    public string CountText => AppText.Format(Language.GroupCount, Count);

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

    /// <summary>
    /// 默认全部折叠，展开一个即折叠其余（手风琴）：
    /// 约 120 个目标一次铺开会淹没首屏，分组头已经给出项数与小计。
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                this.RaiseAndSetIfChanged(ref _isExpanded, value);
                this.RaisePropertyChanged(nameof(ChevronRotation));
                if (value)
                {
                    _onExpanded?.Invoke(this);
                }
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

    internal void RefreshLocalization()
    {
        this.RaisePropertyChanged(nameof(Name));
        this.RaisePropertyChanged(nameof(CountText));
    }

    private void OnCultureChanged(object? sender, EventArgs args) => RefreshLocalization();

    private static string LocalizedName(CleanupDisplayGroup group) => group switch
    {
        CleanupDisplayGroup.TemporaryFiles => AppText.Get(Language.GroupTemporaryFiles),
        CleanupDisplayGroup.SystemCache => AppText.Get(Language.GroupSystemCache),
        CleanupDisplayGroup.RecycleBin => AppText.Get(Language.GroupRecycleBin),
        CleanupDisplayGroup.Browser => AppText.Get(Language.GroupBrowser),
        CleanupDisplayGroup.UserFiles => AppText.Get(Language.GroupUserFiles),
        _ => AppText.Get(Language.GroupSystemFiles)
    };
}
