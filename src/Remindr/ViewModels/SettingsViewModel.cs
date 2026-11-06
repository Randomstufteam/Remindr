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
    private readonly WhatsNewService _whatsNew;
    private readonly NotificationService _notifications;
    private readonly Data.Repositories.ReminderRepo _reminders;
    private readonly IClock _clock;
    private readonly TraySettings _tray;
    private readonly StartWithWindowsService _startup;
    private readonly DailySummaryService _daily;
    private readonly Services.Assistant.AssistantService _assistant;
    private bool _startWithWindows;

    /// <summary>Start Remindr (minimized, in the tray) when the user signs in. Mirrors the real Run value.</summary>
    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (_startWithWindows == value) return;
            _startWithWindows = _startup.Set(value);
            OnPropertyChanged();
        }
    }

    /// <summary>Re-reads the registry (the user may have changed it in Task Manager's Startup apps) when Settings opens.</summary>
    public void RefreshStartWithWindows()
    {
        _startWithWindows = _startup.Read();
        OnPropertyChanged(nameof(StartWithWindows));
    }

    public SettingsViewModel(ThemeService theme, ProfileSession session, ProfilePickerViewModel picker, ProfileDialogs dialogs,
        LocalizationService localization, WelcomeController welcome, UpdateViewModel updates,
        NotificationService notifications, Data.Repositories.ReminderRepo reminders, IClock clock, TraySettings tray,
        StartWithWindowsService startup, WhatsNewService whatsNew, DailySummaryService daily, Services.Assistant.AssistantService assistant,
        ModelPickerViewModel modelPicker, VoiceViewModel voice)
    {
        Models = modelPicker;
        Voice = voice;
        assistant.StateChanged += (_, _) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            OnPropertyChanged(nameof(RunningOn));
            OnPropertyChanged(nameof(HasRunningOn));
            OnPropertyChanged(nameof(GpuNote));
            OnPropertyChanged(nameof(HasGpuNote));
            OnPropertyChanged(nameof(GpuNeedsRestart));
        }));
        _daily = daily;
        _assistant = assistant;
        _whatsNew = whatsNew;
        _tray = tray;
        _startup = startup;
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
            OnPropertyChanged(nameof(RemindUiText));
            OnPropertyChanged(nameof(LicenseText));
            OnPropertyChanged(nameof(CurrentLanguage));
            OnPropertyChanged(nameof(LanguageButtonName));
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

    /// <summary>A toast with the day's summary once a day (global setting, off by default).</summary>
    public bool DailySummary
    {
        get => _daily.Enabled;
        set
        {
            if (_daily.Enabled == value) return;
            _daily.Enabled = value;
            OnPropertyChanged();
        }
    }

    /// <summary>The local AI assistant (global setting, off by default). Turning it off frees the model; the Assistant item leaves the sidebar.</summary>
    public bool AssistantEnabled
    {
        get => _assistant.Enabled;
        set
        {
            if (_assistant.Enabled == value) return;
            _assistant.Enabled = value;
            OnPropertyChanged();
        }
    }

    /// <summary>The model picker (also shown on the Assistant page while no model is installed).</summary>
    public ModelPickerViewModel Models { get; }

    /// <summary>Voice input, speech model and spoken replies.</summary>
    public VoiceViewModel Voice { get; }

    public sealed record GpuOption(Services.Assistant.GpuMode Mode, string Name);

    /// <summary>Auto / On / Off for "Use graphics card".</summary>
    public IReadOnlyList<GpuOption> GpuOptions => new GpuOption[]
    {
        new(Services.Assistant.GpuMode.Auto, Loc.Get("Settings_Assistant_Gpu_Auto")),
        new(Services.Assistant.GpuMode.On, Loc.Get("Settings_Assistant_Gpu_On")),
        new(Services.Assistant.GpuMode.Off, Loc.Get("Settings_Assistant_Gpu_Off")),
    };

    public Services.Assistant.GpuMode Gpu
    {
        get => _assistant.Gpu;
        set { _assistant.Gpu = value; OnPropertyChanged(); OnPropertyChanged(nameof(GpuNeedsRestart)); }
    }

    /// <summary>The native backend is chosen once per process, so a change to or from Off waits for a restart.</summary>
    public bool GpuNeedsRestart => _assistant.GpuNeedsRestart;

    /// <summary>"Running on: NVIDIA ..." or "Running on: CPU" once the model is loaded.</summary>
    public bool HasRunningOn => _assistant.IsLoaded;
    public string RunningOn => _assistant.IsLoaded ? Loc.Format("Assistant_RunningOn", _assistant.Device ?? "CPU") : "";

    /// <summary>Mode On found no dedicated card.</summary>
    public string GpuNote => _assistant.GpuNoteKey is { } k ? Loc.Get(k) : "";
    public bool HasGpuNote => _assistant.GpuNoteKey is not null;

    /// <summary>Every 15 minutes of the day, for the summary time drop-down.</summary>
    public IReadOnlyList<TimeOnly> SummaryTimes { get; } = Enumerable.Range(0, 96).Select(i => new TimeOnly(i / 4, i % 4 * 15)).ToList();

    public TimeOnly SummaryTime
    {
        get => _daily.Time;
        set { _daily.Time = value; OnPropertyChanged(); }
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

    /// <summary>The languages the picker lists: those with a shipped translation (the catalog has more; see <see cref="LocalizationService.Available"/>).</summary>
    public IReadOnlyList<LanguageInfo> Languages => LocalizationService.Available;

    /// <summary>The language in use, for the button that opens the picker.</summary>
    public LanguageInfo CurrentLanguage => LocalizationService.Find(_localization.Code) ?? LocalizationService.Supported[0];

    /// <summary>Screen-reader name of the picker button: "Language: Deutsch. Press to change."</summary>
    public string LanguageButtonName => Loc.Format("Settings_Language_ButtonName", CurrentLanguage.NativeName);

    /// <summary>Switches the app to a language now (the picker calls this when a language is chosen).</summary>
    public void ChooseLanguage(string code)
    {
        _localization.SetLanguage(code);
        OnPropertyChanged(nameof(CurrentLanguage));
        OnPropertyChanged(nameof(LanguageButtonName));
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
    public string RemindUiText => Loc.Format("Settings_RemindUiVersion", RemindUI.Version);
    public string LicenseText => Loc.Format("Settings_License", RepoInfo.License);

    [RelayCommand] private void OpenGitHub() => AppInfo.Open(RepoInfo.RepoUrl);
    [RelayCommand] private void OpenNotices() => AppInfo.Open(RepoInfo.NoticesUrl);

    /// <summary>Shows only the welcome greeting again (no setup, no data changes).</summary>
    [RelayCommand] private void ReplayWelcome() => _welcome.Replay();

    /// <summary>Shows the bundled What's New page again (Settings > About).</summary>
    [RelayCommand] private void ShowWhatsNew() => _whatsNew.RequestShow();
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
