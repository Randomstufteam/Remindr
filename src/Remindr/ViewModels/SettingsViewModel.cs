using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Remindr.Models;
using Remindr.Services;
using Remindr.Views.Dialogs;

namespace Remindr.ViewModels;

/// <summary>A theme with its translated name (the name refreshes when the language changes).</summary>
public sealed partial class ThemeOption : ObservableObject
{
    public ThemeOption(AppTheme theme)
    {
        Theme = theme;
        Loc.CultureChanged += (_, _) => OnPropertyChanged(nameof(Name));
    }

    public AppTheme Theme { get; }
    public string Name => Loc.Get("Settings_Theme_" + Theme);

    /// <summary>Screen readers name the list entry from this.</summary>
    public override string ToString() => Name;
}

public partial class SettingsViewModel : ObservableObject
{
    private readonly ThemeService _theme;
    private readonly ProfileSession _session;
    private readonly ProfilePickerViewModel _picker;
    private readonly ProfileDialogs _dialogs;
    private readonly LocalizationService _localization;
    private readonly WelcomeController _welcome;
    private readonly NotificationService _notifications;
    private readonly Data.Repositories.ReminderRepo _reminders;
    private readonly IClock _clock;
    private readonly TraySettings _tray;

    public SettingsViewModel(ThemeService theme, ProfileSession session, ProfilePickerViewModel picker, ProfileDialogs dialogs,
        LocalizationService localization, WelcomeController welcome, UpdateViewModel updates,
        NotificationService notifications, Data.Repositories.ReminderRepo reminders, IClock clock, TraySettings tray)
    {
        _tray = tray;
        Updates = updates;
        _notifications = notifications;
        _reminders = reminders;
        _clock = clock;
        _welcome = welcome;
        _theme = theme;
        _session = session;
        _picker = picker;
        _dialogs = dialogs;
        _localization = localization;
        _selectedTheme = Themes.First(t => t.Theme == theme.Current);
        _selectedLanguage = LocalizationService.Find(localization.Code) ?? LocalizationService.Supported[0];
        session.CurrentProfileChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CurrentProfileName));
            OnPropertyChanged(nameof(SignedInText));
            OnPropertyChanged(nameof(CurrentColor));
            OnPropertyChanged(nameof(CurrentInitial));
            // Re-importing a picture keeps the same 'file:avatars/id.png' value, which the avatar control would treat as
            // unchanged; blank it for a moment so the new file is re-read.
            _avatarBlank = true;
            OnPropertyChanged(nameof(CurrentAvatar));
            _avatarBlank = false;
            OnPropertyChanged(nameof(CurrentAvatar));
            OnPropertyChanged(nameof(HasPin));
            OnPropertyChanged(nameof(HasNoPin));
            OnPropertyChanged(nameof(PinStatusText));
        };
        // Strings composed here are recomputed when the language changes (the {l:Loc} ones refresh by themselves).
        Loc.CultureChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(SignedInText));
            OnPropertyChanged(nameof(PinStatusText));
            OnPropertyChanged(nameof(VersionText));
            OnPropertyChanged(nameof(LicenseText));
        };
    }

    /// <summary>Keep running in the tray when the window is closed (saved at once).</summary>
    public bool CloseToTray
    {
        get => _tray.CloseToTray;
        set
        {
            if (_tray.CloseToTray == value) return;
            _tray.CloseToTray = value;
            OnPropertyChanged();
        }
    }

    /// <summary>The update toggle and the About card's "Check for updates" row.</summary>
    public UpdateViewModel Updates { get; }

    public string CurrentProfileName => _session.Current?.Name ?? "";
    public string SignedInText => Loc.Format("Settings_SignedIn", CurrentProfileName);
    public string CurrentColor => _session.Current?.Color ?? "#3B82F6";
    public string CurrentInitial => _session.Current?.AvatarInitial ?? "?";
    private bool _avatarBlank;
    public string? CurrentAvatar => _avatarBlank ? null : _session.Current?.Avatar;

    public bool HasPin => _session.Current?.HasPin ?? false;
    public bool HasNoPin => !HasPin;
    public string PinStatusText => Loc.Format("Settings_PinStatus", Loc.Get(HasPin ? "Common_On" : "Common_Off"));

    public IReadOnlyList<ThemeOption> Themes { get; } =
        new[] { AppTheme.Light, AppTheme.Dark, AppTheme.System }.Select(t => new ThemeOption(t)).ToList();

    [ObservableProperty] private ThemeOption _selectedTheme;

    partial void OnSelectedThemeChanged(ThemeOption value)
    {
        if (value is not null) _theme.Set(value.Theme);
    }

    // ---- Language ----------------------------------------------------------------------------------------

    /// <summary>Every supported language (native name first, English name beside it).</summary>
    public IReadOnlyList<LanguageInfo> Languages => LocalizationService.Supported;

    [ObservableProperty] private LanguageInfo _selectedLanguage;

    partial void OnSelectedLanguageChanged(LanguageInfo value)
    {
        // The combo can briefly report null while its items are rebuilt; keep the current choice then.
        if (value is not null) _localization.SetLanguage(value.Code);
    }

    [RelayCommand]
    private void SuggestTranslation() => AppInfo.Open(FeedbackLinks.TranslationUrl(_localization.Code));

    // ---- Profile -----------------------------------------------------------------------------------------

    [RelayCommand]
    private void SwitchProfile() => _picker.Show();

    [RelayCommand]
    private async Task EditCurrentProfileAsync()
    {
        if (_session.Current is null) return;
        var saved = await _dialogs.ShowEditAsync(_session.Current);
        if (saved is not null) _session.RefreshCurrent();
    }

    [RelayCommand]
    private async Task ChangePictureAsync()
    {
        if (_session.Current is null) return;
        var saved = await _dialogs.ShowAvatarAsync(_session.Current);
        if (saved is not null) _session.RefreshCurrent();
    }

    public string VersionText => Loc.Format("Settings_Version", AppInfo.Version);
    public string LicenseText => Loc.Format("Settings_License", RepoInfo.License);

    [RelayCommand] private void OpenGitHub() => AppInfo.Open(RepoInfo.RepoUrl);
    [RelayCommand] private void OpenNotices() => AppInfo.Open(RepoInfo.NoticesUrl);

    /// <summary>Shows only the welcome greeting again (no setup, no data changes).</summary>
    [RelayCommand] private void ReplayWelcome() => _welcome.Replay();
    /// <summary>Shows a toast for the next upcoming open reminder of this profile (or a sample one), buttons included.</summary>
    [RelayCommand]
    private void SendTestNotification()
    {
        Reminder? next = null;
        try
        {
            if (_session.Current is not null)
                next = _reminders.ListAll().FirstOrDefault(r => !r.IsCompleted && NotificationPlanner.DueLocal(r) >= _clock.Now);
        }
        catch (Exception ex) { ErrorLog.Write(ex, "Finding a reminder for the test notification"); }
        _notifications.ShowTest(next);
    }

    [RelayCommand] private void ReportBug() => AppInfo.Open(FeedbackLinks.BugReportUrl(AppInfo.Version, AppInfo.OsText));
    [RelayCommand] private void SuggestFeature() => AppInfo.Open(FeedbackLinks.FeatureRequestUrl());

    [RelayCommand]
    private void OpenLogFolder()
    {
        var dir = Path.GetDirectoryName(ErrorLog.FilePath);
        if (string.IsNullOrEmpty(dir)) return;
        try { Directory.CreateDirectory(dir); } catch { /* opening may still work */ }
        AppInfo.Open(dir);
    }

    [RelayCommand] private Task SetPinAsync() => RunPinAsync(PinMode.Set);
    [RelayCommand] private Task ChangePinAsync() => RunPinAsync(PinMode.Change);
    [RelayCommand] private Task RemovePinAsync() => RunPinAsync(PinMode.Remove);

    private async Task RunPinAsync(PinMode mode)
    {
        if (_session.Current is not { } profile) return;
        if (await _dialogs.ShowPinAsync(profile, mode)) _session.RefreshCurrent();
    }
}
