using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Remindr.Services;

namespace Remindr.ViewModels;

/// <summary>What the update banner (top of the content) and the Settings update rows show. Text is composed here so it follows the language.</summary>
public sealed partial class UpdateViewModel : ObservableObject
{
    private readonly UpdateService _service;

    // Cached so the frequent progress ticks do not hit the settings DB; refreshed on toggle and on an explicit check.
    private bool _checkEnabled;

    public UpdateViewModel(UpdateService service)
    {
        _service = service;
        _checkEnabled = service.CheckEnabled;
        service.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UpdateService.CheckEnabled)) _checkEnabled = _service.CheckEnabled;
            RaiseAll();
        };
        Loc.CultureChanged += (_, _) => RaiseAll();
    }

    private void RaiseAll()
    {
        OnPropertyChanged(string.Empty);
        CheckNowCommand.NotifyCanExecuteChanged();
    }

    private UpdateState State => _service.State;
    private string VersionText => _service.Release?.Version.ToString() ?? "";

    // ---- Update card ---------------------------------------------------------------------------------------

    public bool IsBannerVisible => _service.ShowBanner;

    /// <summary>Offer: Update / What's new / Skip.</summary>
    public bool IsOffer => State == UpdateState.Available;

    /// <summary>Downloading or checking the download: progress bar + Cancel.</summary>
    public bool IsDownloading => State is UpdateState.Downloading or UpdateState.Verifying;

    /// <summary>Verified: "Restart and update".</summary>
    public bool IsReady => State is UpdateState.ReadyToInstall or UpdateState.Launching or UpdateState.WouldLaunch;
    public bool CanInstall => State == UpdateState.ReadyToInstall;

    public bool IsError => State is UpdateState.DownloadFailed or UpdateState.HashMismatch;
    public bool IsIndeterminate => State == UpdateState.Verifying || _service.Progress <= 0;
    public double ProgressPercent => Math.Round(_service.Progress * 100);

    public string TitleText => State switch
    {
        UpdateState.Downloading => Loc.Format("Update_Downloading", ProgressPercent),
        UpdateState.Verifying => Loc.Get("Update_Verifying"),
        UpdateState.ReadyToInstall or UpdateState.Launching or UpdateState.WouldLaunch => Loc.Get("Update_Ready"),
        UpdateState.HashMismatch => Loc.Get("Update_HashFailed"),
        UpdateState.DownloadFailed => Loc.Get("Update_DownloadFailed"),
        _ => Loc.Get("Update_Title"),
    };

    /// <summary>"Remindr 0.4.0 · you have 0.3.0".</summary>
    public string VersionsText => Loc.Format("Update_Versions", VersionText, AppInfo.Version);
    public bool ShowVersions => State is UpdateState.Available or UpdateState.ReadyToInstall or UpdateState.Launching or UpdateState.WouldLaunch;

    /// <summary>Debug dry run only: where the verified installer is, instead of running it.</summary>
    public string DetailText => State == UpdateState.WouldLaunch ? "would launch " + _service.LaunchedPath : "";
    public bool HasDetail => DetailText.Length > 0;

    [RelayCommand] private Task UpdateNowAsync() => _service.UpdateNowAsync();
    [RelayCommand] private Task InstallAsync() => _service.InstallAsync();
    [RelayCommand] private void Cancel() => _service.CancelDownload();
    [RelayCommand] private void WhatsNew() { if (_service.Release is { } r) AppInfo.Open(r.HtmlUrl); }
    [RelayCommand] private void Later() => _service.Later();
    [RelayCommand] private void Skip() => _service.Skip();
    // ---- Settings ------------------------------------------------------------------------------------------

    public bool CheckEnabled
    {
        get => _checkEnabled;
        set { _service.CheckEnabled = value; _checkEnabled = _service.CheckEnabled; OnPropertyChanged(); }
    }

    public bool IsChecking => State == UpdateState.Checking;

    public string StatusText
    {
        get
        {
            if (State == UpdateState.Checking) return Loc.Get("Settings_Update_Checking");
            if (_service.Release is not null && State is UpdateState.Available or UpdateState.UpToDate)
                return Loc.Format("Settings_Update_Available", VersionText);
            if (State == UpdateState.CheckFailed) return Loc.Get("Settings_Update_Failed");
            if (State == UpdateState.UpToDate && _service.LastChecked is { } t)
                return Loc.Format("Settings_Update_UpToDate", t.ToString("t", Loc.Culture));
            return "";
        }
    }

    public bool HasStatus => StatusText.Length > 0;

    [RelayCommand(CanExecute = nameof(CanCheckNow))]
    private Task CheckNowAsync()
    {
        _checkEnabled = _service.CheckEnabled;
        return _service.CheckAsync(manual: true);
    }

    private bool CanCheckNow() => State is not (UpdateState.Checking or UpdateState.Downloading or UpdateState.Verifying or UpdateState.ReadyToInstall or UpdateState.Launching);
}
