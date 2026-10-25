using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Remindr.Data.Repositories;
using Remindr.Models;
using Remindr.Services;
using Remindr.Views.Dialogs;

namespace Remindr.ViewModels;

/// <summary>Drives the full-window profile picker overlay (tiles, PIN prompt, add/edit/delete).</summary>
public sealed partial class ProfilePickerViewModel : ObservableObject
{
    private readonly ProfileRepo _profiles;
    private readonly ProfileSession _session;
    private readonly ProfileDialogs _dialogs;

    public ProfilePickerViewModel(ProfileRepo profiles, ProfileSession session, ProfileDialogs dialogs)
    {
        _profiles = profiles;
        _session = session;
        _dialogs = dialogs;
        Loc.CultureChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(PinPrompt));
            OnPropertyChanged(nameof(PinError));
        };
    }

    public ObservableCollection<object> Tiles { get; } = new();

    [ObservableProperty] private bool _isVisible;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsPinPromptOpen))]
    private ProfileTileViewModel? _pinTarget;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(PinLength))]
    private string _pinInput = "";

    /// <summary>Digits typed so far (drives the filled dots).</summary>
    public int PinLength => PinInput.Length;

    /// <summary>Raised on every wrong PIN (PinError alone doesn't change on a repeat, so the view can't rely on it to shake).</summary>
    public event EventHandler? PinRejected;

    partial void OnPinInputChanged(string value)
    {
        // The 4th digit unlocks automatically; a short beat lets the last dot fill first.
        if (value.Length == 4 && PinTarget is not null) _ = AutoSubmitAsync();
    }

    private async Task AutoSubmitAsync()
    {
        await Task.Delay(90);
        if (PinTarget is not null && PinInput.Length == 4) await SubmitPinAsync();
    }

    public bool IsPinPromptOpen => PinTarget is not null;

    /// <summary>True when a profile is already active, so the picker can be dismissed without choosing again.</summary>
    [ObservableProperty] private bool _canClose;

    /// <summary>Reloads the tiles and shows the overlay.</summary>
    public void Show()
    {
        CancelPin();
        Reload();
        CanClose = _session.Current is not null;
        IsVisible = true;
    }

    /// <summary>Back to the current profile (only when one is active).</summary>
    [RelayCommand]
    private void Close()
    {
        if (_session.Current is null) return;
        CancelPin();
        IsVisible = false;
    }

    public void Reload()
    {
        var all = _profiles.List();
        var lastId = _session.Current?.Id ?? _session.LastProfileId;
        var canDelete = all.Count > 1;
        Tiles.Clear();
        foreach (var p in all.OrderBy(p => p.Id == lastId ? 0 : 1).ThenBy(p => p.Id))
            Tiles.Add(new ProfileTileViewModel(p, p.Id == lastId, canDelete));
        Tiles.Add(new AddTileViewModel());
    }

    /// <summary>"Enter your 4-digit PIN" to open a profile, or the longer text when the PIN guards editing / deleting it.</summary>
    public string PinPrompt => Loc.Get(_afterUnlock is null ? "Picker_PinPromptEnter" : "Picker_PinPromptManage");

    /// <summary>"Incorrect PIN. Try again." in the current language, or null (kept as text so a language change updates it).</summary>
    public string? PinError => _pinWrong ? Loc.Get("Picker_PinWrong") : null;
    private bool _pinWrong;

    /// <summary>What to run after a correct PIN. Null = enter the profile.</summary>
    private Func<ProfileTileViewModel, Task>? _afterUnlock;

    /// <summary>A PIN-protected profile needs its PIN for anything except when it is the one already unlocked.</summary>
    private bool NeedsPin(ProfileTileViewModel tile) => tile.HasPin && tile.Profile.Id != _session.Current?.Id;

    private void RequestPin(ProfileTileViewModel tile, Func<ProfileTileViewModel, Task>? afterUnlock)
    {
        _afterUnlock = afterUnlock;
        OnPropertyChanged(nameof(PinPrompt));
        PinInput = "";
        SetPinWrong(false);
        PinTarget = tile;
    }

    [RelayCommand]
    private void Select(ProfileTileViewModel? tile)
    {
        if (tile is null) return;
        if (tile.HasPin) RequestPin(tile, null);
        else Enter(tile.Profile);
    }

    [RelayCommand]
    private async Task SubmitPinAsync()
    {
        if (PinTarget is not { } tile) return;
        var ok = PinHasher.Verify(PinInput, tile.Profile.PinHash, tile.Profile.PinSalt);
        PinInput = ""; // don't keep the typed PIN around either way
        if (!ok)
        {
            SetPinWrong(true);
            PinRejected?.Invoke(this, EventArgs.Empty);
            return;
        }

        var after = _afterUnlock;
        CancelPin();
        if (after is null) Enter(tile.Profile);
        else await after(tile);
    }

    [RelayCommand]
    private void CancelPin()
    {
        _afterUnlock = null;
        OnPropertyChanged(nameof(PinPrompt));
        PinTarget = null;
        PinInput = "";
        SetPinWrong(false);
    }

    private void SetPinWrong(bool wrong)
    {
        _pinWrong = wrong;
        OnPropertyChanged(nameof(PinError));
    }

    private void Enter(Profile profile)
    {
        _session.SwitchTo(profile);
        IsVisible = false;
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        var created = await _dialogs.ShowEditAsync(null);
        if (created is not null) Reload();
    }

    [RelayCommand]
    private async Task EditAsync(ProfileTileViewModel? tile)
    {
        if (tile is null) return;
        if (NeedsPin(tile)) { RequestPin(tile, EditUnlockedAsync); return; }
        await EditUnlockedAsync(tile);
    }

    private async Task EditUnlockedAsync(ProfileTileViewModel tile)
    {
        var saved = await _dialogs.ShowEditAsync(tile.Profile);
        if (saved is null) return;
        if (_session.Current?.Id == saved.Id) _session.RefreshCurrent();
        Reload();
    }

    [RelayCommand]
    private async Task DeleteAsync(ProfileTileViewModel? tile)
    {
        if (tile is null) return;
        if (NeedsPin(tile)) { RequestPin(tile, DeleteUnlockedAsync); return; }
        await DeleteUnlockedAsync(tile);
    }

    private async Task DeleteUnlockedAsync(ProfileTileViewModel tile)
    {
        if (!await _dialogs.ConfirmDeleteAsync(tile.Profile)) return;
        var wasCurrent = _session.Current?.Id == tile.Profile.Id;
        Reload();
        if (wasCurrent)
        {
            _session.Clear(); // shell re-shows the picker
            CanClose = false;
        }
    }
}

/// <summary>Placeholder item that renders as the Add profile tile inside the tile list.</summary>
public sealed class AddTileViewModel { }


