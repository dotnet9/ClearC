using System.Collections.ObjectModel;
using System.Windows.Input;

namespace ClearC.Desktop.ViewModels;

/// <summary>总览区 + 分组栏 + 结果列表（三个子视图共用这一个 DataContext）。</summary>
public sealed class CleanupWorkspaceViewModel : MainWindowSectionViewModel
{
    public CleanupWorkspaceViewModel(MainWindowViewModel owner)
        : base(
            owner,
            nameof(DriveTitle),
            nameof(DriveInfo),
            nameof(UsedRatio),
            nameof(UsedPercent),
            nameof(DiskUsedText),
            nameof(DiskFreeText),
            nameof(HeroLabel),
            nameof(HeroValue),
            nameof(IsHeroPlaceholder),
            nameof(PrimaryButtonText),
            nameof(PrimaryIconKey),
            nameof(IsPrimaryGlowing),
            nameof(IsPrimaryEnabled),
            nameof(SecondaryButtonText),
            nameof(IsSecondaryEnabled),
            nameof(IsAwaitingCommandStop),
            nameof(IsProgressVisible),
            nameof(ProgressValue),
            nameof(ProgressText),
            nameof(ProgressBarWidth),
            nameof(SelectedSummary),
            nameof(CanSelectAll),
            nameof(IsAllSelected),
            nameof(HideZeroByteItems),
            nameof(SkipSystemAnalysis),
            nameof(IsEmptyVisible),
            nameof(IsListVisible),
            nameof(IsSweepVisible),
            nameof(IsGhostVisible),
            nameof(GhostText),
            nameof(IsElevationBannerVisible),
            nameof(ElevationBannerText))
    {
    }

    public ObservableCollection<CleanupGroupViewModel> Groups => Owner.Groups;
    public ICommand PrimaryCommand => Owner.PrimaryCommand;
    public ICommand SecondaryCommand => Owner.SecondaryCommand;
    public ICommand RestartElevatedCommand => Owner.RestartElevatedCommand;

    public string DriveTitle => Owner.DriveTitle;
    public string DriveInfo => Owner.DriveInfo;
    public double UsedRatio => Owner.UsedRatio;
    public string UsedPercent => Owner.UsedPercent;
    public string DiskUsedText => Owner.DiskUsedText;
    public string DiskFreeText => Owner.DiskFreeText;
    public string HeroLabel => Owner.HeroLabel;
    public string HeroValue => Owner.HeroValue;
    public bool IsHeroPlaceholder => Owner.IsHeroPlaceholder;
    public string PrimaryButtonText => Owner.PrimaryButtonText;
    public string PrimaryIconKey => Owner.PrimaryIconKey;
    public bool IsPrimaryGlowing => Owner.IsPrimaryGlowing;
    public bool IsPrimaryEnabled => Owner.IsPrimaryEnabled;
    public string SecondaryButtonText => Owner.SecondaryButtonText;
    public bool IsSecondaryEnabled => Owner.IsSecondaryEnabled;
    public bool IsAwaitingCommandStop => Owner.IsAwaitingCommandStop;
    public bool IsProgressVisible => Owner.IsProgressVisible;
    public double ProgressValue => Owner.ProgressValue;
    public string ProgressText => Owner.ProgressText;
    public double ProgressBarWidth => Owner.ProgressBarWidth;
    public string SelectedSummary => Owner.SelectedSummary;
    public bool CanSelectAll => Owner.CanSelectAll;

    public bool IsAllSelected
    {
        get => Owner.IsAllSelected;
        set => Owner.IsAllSelected = value;
    }

    public bool HideZeroByteItems
    {
        get => Owner.HideZeroByteItems;
        set => Owner.HideZeroByteItems = value;
    }

    /// <summary>快速模式：跳过 DISM / vssadmin 分析项（默认开）。</summary>
    public bool SkipSystemAnalysis
    {
        get => Owner.SkipSystemAnalysis;
        set => Owner.SkipSystemAnalysis = value;
    }

    public bool IsEmptyVisible => Owner.IsEmptyVisible;
    public bool IsListVisible => Owner.IsListVisible;
    public bool IsSweepVisible => Owner.IsSweepVisible;
    public bool IsGhostVisible => Owner.IsGhostVisible;
    public string GhostText => Owner.GhostText;
    public bool IsElevationBannerVisible => Owner.IsElevationBannerVisible;
    public string ElevationBannerText => Owner.ElevationBannerText;
}
