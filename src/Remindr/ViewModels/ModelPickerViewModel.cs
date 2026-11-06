using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Remindr.Services;
using Remindr.Services.Assistant;

namespace Remindr.ViewModels;

/// <summary>One model size card of the picker: greyed out (and says why) when this PC has too little memory.</summary>
public sealed class TierCard : ObservableObject
{
    private readonly ModelPickerViewModel _owner;

    public TierCard(ModelPickerViewModel owner, ModelTier tier, bool canRun)
    {
        _owner = owner;
        Tier = tier;
        CanRun = canRun;
    }

    public void Refresh() => OnPropertyChanged(string.Empty);

    public ModelTier Tier { get; }
    public bool CanRun { get; }
    public bool IsInstalled => _owner.InstalledId == Tier.Id;
    public string Name => Loc.Get("Assistant_Tier_" + Tier.Id + "_Name");
    public string Note => Loc.Get("Assistant_Tier_" + Tier.Id + "_Note");
    public string Detail => Loc.Format("Assistant_Tier_Detail", ModelPickerViewModel.Size(Tier.Bytes), Tier.MinRamBytes >> 30);
    public string Reason => CanRun ? "" : Loc.Get("Assistant_Tier_TooLittleRam");
    public string InstalledText => IsInstalled ? Loc.Get("Assistant_Tier_Installed") : "";
    /// <summary>Screen-reader name: the whole card in one sentence.</summary>
    public string AutomationName => $"{Name}. {Note}. {Detail}. {Reason} {InstalledText}".Trim();

    public bool IsSelected
    {
        get => _owner.SelectedId == Tier.Id;
        set { if (value) _owner.Select(this); }
    }
}

/// <summary>The model picker shared by Settings and the Assistant page: three size cards, Download / Switch, progress with Pause, Resume and Cancel, and Remove.</summary>
public sealed partial class ModelPickerViewModel : ObservableObject
{
    private readonly ModelManager _models;

    public ModelPickerViewModel(ModelManager models)
    {
        _models = models;
        models.Changed += (_, _) => OnUi(Refresh);
        Loc.CultureChanged += (_, _) => OnUi(Refresh);
        Cards = ModelCatalog.Tiers.Select(t => new TierCard(this, t, ModelCatalog.CanRun(t, models.TotalRam))).ToList();
    }

    public List<TierCard> Cards { get; }
    public string? InstalledId => _models.Installed?.Id;
    public string SelectedId => _models.Selected.Id;

    private static void OnUi(Action action)
    {
        if (Application.Current?.Dispatcher is { } d && !d.CheckAccess()) d.BeginInvoke(action);
        else action();
    }

    public void Select(TierCard card)
    {
        if (_models.IsBusy || !card.CanRun || _models.Selected.Id == card.Tier.Id) return;
        _models.Selected = card.Tier;
    }

    public static string Size(long bytes) => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} GB" : $"{bytes / (double)(1L << 20):0} MB";

    private ModelTier? Target => _models.Target ?? _models.Selected;
    public bool IsDownloading => _models.State == ModelDownloadState.Downloading;
    public bool IsVerifying => _models.State == ModelDownloadState.Verifying;
    public bool IsPaused => _models.State == ModelDownloadState.Paused;
    /// <summary>The progress row (bar and buttons) is shown while a download is running or paused.</summary>
    public bool ShowProgress => _models.State is ModelDownloadState.Downloading or ModelDownloadState.Verifying or ModelDownloadState.Paused;
    public bool CanChoose => !ShowProgress;
    /// <summary>Download (nothing installed) or Switch (another tier installed): shown when the selected tier is not installed and nothing is running.</summary>
    public bool CanStart => !ShowProgress && _models.Installed?.Id != _models.Selected.Id;
    public string StartLabel => Loc.Get(_models.Installed is null ? "Assistant_Dl_Download" : "Assistant_Dl_Switch");
    public bool CanRemove => !ShowProgress && _models.Installed is not null;
    /// <summary>Something to pick or manage: no model installed, or a download going on, or (in Settings) any state.</summary>
    public bool NeedsModel => _models.Installed is null;
    public double Progress => _models.Progress;
    public string ProgressText => IsVerifying ? Loc.Get("Assistant_Dl_Verifying")
        : Loc.Format(IsPaused ? "Assistant_Dl_Paused" : "Assistant_Dl_Progress", Size(_models.BytesDone), Size(Target!.Bytes));
    public string? ErrorText => _models.ErrorKey is { } k ? Loc.Get(k) : null;
    public bool HasError => _models.ErrorKey is not null;

    private void Refresh()
    {
        foreach (var c in Cards) c.Refresh();
        OnPropertyChanged(string.Empty);
    }

    [RelayCommand]
    private Task Download() => _models.StartAsync(_models.Selected);

    [RelayCommand]
    private Task Resume() => _models.StartAsync(_models.Target ?? _models.Selected);

    [RelayCommand] private void Pause() => _models.Pause();
    [RelayCommand] private void CancelDownload() => _models.Cancel();
    [RelayCommand] private void Remove() => _models.Remove();

    /// <summary>"Model by Liquid AI · LFM Open License v1.0": opens the installed licence text (the web copy if the file isn't next to the exe).</summary>
    [RelayCommand]
    private void OpenLicense()
    {
        try
        {
            var file = Path.Combine(AppContext.BaseDirectory, ModelCatalog.LicenseFile);
            var target = File.Exists(file) ? file : $"https://huggingface.co/{ModelCatalog.Repo}/blob/main/LICENSE";
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) { ErrorLog.Write(ex, "Open model licence"); }
    }
}
