using System.Collections.ObjectModel;
using System.Windows.Input;

namespace ClearC.Desktop.ViewModels;

public sealed class CleanupWorkspaceViewModel : MainWindowSectionViewModel
{
    public CleanupWorkspaceViewModel(MainWindowViewModel owner)
        : base(
            owner,
            nameof(DriveTitle),
            nameof(DriveInfo),
            nameof(UsedRatio),
            nameof(UsedPercent),
            nameof(DiskUsage),
            nameof(HeroLabel),
            nameof(HeroValue),
            nameof(PrimaryButtonText),
            nameof(IsPrimaryEnabled),
            nameof(SecondaryButtonText),
            nameof(IsSecondaryEnabled),
            nameof(IsProgressVisible),
            nameof(ProgressValue),
            nameof(ProgressText),
            nameof(SelectedSummary),
            nameof(CanSelectAll),
            nameof(IsAllSelected),
            nameof(IsEmptyVisible),
            nameof(IsScanningEmptyVisible),
            nameof(IsListVisible))
    {
    }

    public ObservableCollection<CleanupItemViewModel> VisibleItems => Owner.VisibleItems;
    public ObservableCollection<CategoryFilterViewModel> Filters => Owner.Filters;
    public ICommand PrimaryCommand => Owner.PrimaryCommand;
    public ICommand SecondaryCommand => Owner.SecondaryCommand;
    public ICommand SelectFilterCommand => Owner.SelectFilterCommand;
    public string DriveTitle => Owner.DriveTitle;
    public string DriveInfo => Owner.DriveInfo;
    public double UsedRatio => Owner.UsedRatio;
    public string UsedPercent => Owner.UsedPercent;
    public string DiskUsage => Owner.DiskUsage;
    public string HeroLabel => Owner.HeroLabel;
    public string HeroValue => Owner.HeroValue;
    public string PrimaryButtonText => Owner.PrimaryButtonText;
    public bool IsPrimaryEnabled => Owner.IsPrimaryEnabled;
    public string SecondaryButtonText => Owner.SecondaryButtonText;
    public bool IsSecondaryEnabled => Owner.IsSecondaryEnabled;
    public bool IsProgressVisible => Owner.IsProgressVisible;
    public double ProgressValue => Owner.ProgressValue;
    public string ProgressText => Owner.ProgressText;
    public string SelectedSummary => Owner.SelectedSummary;
    public bool CanSelectAll => Owner.CanSelectAll;
    public bool IsAllSelected
    {
        get => Owner.IsAllSelected;
        set => Owner.IsAllSelected = value;
    }
    public bool IsEmptyVisible => Owner.IsEmptyVisible;
    public bool IsScanningEmptyVisible => Owner.IsScanningEmptyVisible;
    public bool IsListVisible => Owner.IsListVisible;
}
