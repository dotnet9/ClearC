using System.Collections.Generic;
using System.Diagnostics;
using ClearC.Desktop.Services;
using ClearC.Desktop.I18n;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Input;
using ReactiveUI;

namespace ClearC.Desktop.ViewModels;

/// <summary>
/// 更新状态机：检查 GitHub 最新版 → 下载（带进度）→ 运行安装器。
/// 任何失败都转成可直接展示的中文提示，不抛出。
/// </summary>
public sealed class UpdateViewModel : ReactiveObject
{
    private readonly IUpdateChecker _checker;
    private readonly IUpdateDownloader _downloader;
    private readonly HttpClient _http;
    private readonly Action<string, string, Exception?> _log;
    private CancellationTokenSource? _downloadCancellation;
    private double _downloadProgress;
    private bool _isChecking;
    private bool _isDownloading;
    private string? _downloadedPath;
    private UpdateInfo? _pendingUpdate;
    private string _resultText = string.Empty;

    public UpdateViewModel(Action<string, string, Exception?> log)
    {
        _log = log;
        _http = new HttpClient();
        _checker = new UpdateChecker(_http, "dotnet9", "ClearC", log: (message, exception) => _log("WARN", message, exception));
        _downloader = new UpdateDownloader(_http);

        CheckCommand = ReactiveCommand.CreateFromTask(() => CheckAsync());
        DownloadCommand = ReactiveCommand.CreateFromTask(() => DownloadAsync());
        InstallCommand = ReactiveCommand.Create(InstallDownloaded);
        OpenPageCommand = ReactiveCommand.Create(OpenReleasePage);
        AppLocalizer.Instance.CultureChanged += OnCultureChanged;
    }

    public ICommand CheckCommand { get; }
    public ICommand DownloadCommand { get; }
    public ICommand InstallCommand { get; }
    public ICommand OpenPageCommand { get; }

    public bool IsChecking
    {
        get => _isChecking;
        private set => this.RaiseAndSetIfChanged(ref _isChecking, value);
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        private set
        {
            if (this.RaiseAndSetIfChanged(ref _isDownloading, value))
            {
                this.RaisePropertyChanged(nameof(CanDownload));
            }
        }
    }

    public bool IsReady => !string.IsNullOrWhiteSpace(_downloadedPath) && File.Exists(_downloadedPath);

    public bool UpdateAvailable => _pendingUpdate is not null;

    /// <summary>状态栏常驻按钮的文案：平时是检查更新，发现新版后变成版本徽标。</summary>
    public string ButtonText => UpdateAvailable ? AppText.Format(Language.UpdateNewVersion, _pendingUpdate!.Tag) : AppText.Get(Language.UpdateCheck);

    public string ResultText => _resultText;

    public double DownloadProgress => _downloadProgress;

    public string ProgressText => IsDownloading
        ? _downloadProgress > 0 ? AppText.Format(Language.UpdateDownloading, _downloadProgress.ToString("0", AppLocalizer.Instance.Culture)) : AppText.Get(Language.UpdatePreparing)
        : IsReady ? AppText.Get(Language.UpdateProgressReady) : string.Empty;

    public bool CanDownload => UpdateAvailable && !IsDownloading && !IsReady &&
        !string.IsNullOrWhiteSpace(_pendingUpdate?.AssetUrl) &&
        !string.IsNullOrWhiteSpace(_pendingUpdate?.ChecksumUrl);

    /// <summary>没有匹配资产或缺少校验文件时，只能引导用户去 Release 页面。</summary>
    public bool NeedsReleasePage => UpdateAvailable && !IsDownloading && !IsReady && !CanDownload;

    public async Task CheckAsync()
    {
        if (IsChecking || IsDownloading)
        {
            return;
        }

        IsChecking = true;
        try
        {
            var current = ParseCurrentVersion();
            UpdateCheckResult result = await _checker.CheckAsync(current).ConfigureAwait(true);

            if (!result.Succeeded)
            {
                SetResult(AppText.Get(Language.UpdateError));
                return;
            }

            if (result.Update is null)
            {
                _pendingUpdate = null;
                SetResult(AppText.Format(Language.UpdateLatest, current));
                return;
            }

            _pendingUpdate = result.Update;
            SetResult(NeedsReleasePage
                ? AppText.Get(Language.UpdateNoAsset)
                : AppText.Format(Language.UpdateAvailable, result.Update.Tag));
        }
        finally
        {
            IsChecking = false;
        }
    }

    public async Task DownloadAsync()
    {
        if (IsDownloading || _pendingUpdate?.AssetUrl is null)
        {
            if (NeedsReleasePage)
            {
                OpenReleasePage();
            }
            return;
        }

        _downloadCancellation?.Dispose();
        _downloadCancellation = new CancellationTokenSource();
        _downloadProgress = 0;
        IsDownloading = true;
        RaiseAll();

        try
        {
            var progress = new Progress<UpdateDownloadProgress>(value =>
            {
                _downloadProgress = value.Percentage ?? _downloadProgress;
                this.RaisePropertyChanged(nameof(DownloadProgress));
                this.RaisePropertyChanged(nameof(ProgressText));
            });

            UpdateDownloadResult download = await _downloader.DownloadAsync(
                _pendingUpdate, progress, _downloadCancellation.Token).ConfigureAwait(true);
            _downloadedPath = download.FilePath;
            SetResult(AppText.Format(Language.UpdateDownloaded, download.FileName));
        }
        catch (OperationCanceledException)
        {
            SetResult(AppText.Get(Language.UpdateCanceled));
        }
        catch (Exception exception)
        {
            SetResult(AppText.Format(Language.UpdateDownloadFailed, exception.Message));
        }
        finally
        {
            IsDownloading = false;
            RaiseAll();
        }
    }

    /// <summary>运行下载好的安装器（Inno Setup），主程序退出让安装器接管。</summary>
    public void InstallDownloaded()
    {
        string? path = _downloadedPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            SetResult(AppText.Get(Language.UpdateInstallMissing));
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }
        catch (Exception exception)
        {
            SetResult(AppText.Format(Language.UpdateInstallFailed, exception.Message));
        }
    }

    public void OpenReleasePage()
    {
        if (_pendingUpdate?.PageUrl is string url)
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }

    private static Version ParseCurrentVersion()
    {
        var informational = typeof(UpdateViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return VersionUtil.Parse(informational) ?? new Version(0, 1, 0);
    }

    private void SetResult(string text)
    {
        _resultText = text;
        this.RaisePropertyChanged(nameof(ResultText));
        this.RaisePropertyChanged(nameof(UpdateAvailable));
        this.RaisePropertyChanged(nameof(ButtonText));
        this.RaisePropertyChanged(nameof(IsReady));
        this.RaisePropertyChanged(nameof(CanDownload));
        this.RaisePropertyChanged(nameof(NeedsReleasePage));
        this.RaisePropertyChanged(nameof(ProgressText));
        _log("INFO", text, null);
    }

    private void RaiseAll()
    {
        this.RaisePropertyChanged(nameof(IsReady));
        this.RaisePropertyChanged(nameof(CanDownload));
        this.RaisePropertyChanged(nameof(ProgressText));
    }

    private void OnCultureChanged(object? sender, EventArgs args)
    {
        this.RaisePropertyChanged(nameof(ButtonText));
        this.RaisePropertyChanged(nameof(ResultText));
        this.RaisePropertyChanged(nameof(ProgressText));
    }
}
