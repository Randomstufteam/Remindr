using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Remindr.Data.Repositories;
using Remindr.Models;
using Remindr.Services;

namespace Remindr.ViewModels;

/// <summary>The five pages of first-run setup, in order.</summary>
public enum SetupStep { Language, Appearance, Profile, Pin, Done }

/// <summary>One progress dot. The automation name reads "Step 2 of 5" and follows the language.</summary>
public sealed partial class StepDot : ObservableObject
{
    public StepDot(int index, int count)
    {
        Index = index;
        Count = count;
        Loc.CultureChanged += (_, _) => OnPropertyChanged(nameof(AutomationName));
    }

    public int Index { get; }
    public int Count { get; }
    public string AutomationName => Loc.Format("Setup_StepOf", Index + 1, Count);

    [ObservableProperty] private bool _isCurrent;
    [ObservableProperty] private bool _isDone;

    public override string ToString() => AutomationName;
}

/// <summary>
/// First-run setup as a state machine, with no WPF in it: Language, Appearance, Profile, Pin (optional), Done.
/// Choosing a language or theme is previewed live through the two callbacks, but nothing is stored until <see cref="Complete"/>, which in
/// one transaction creates the profile (avatar, PIN hash, the three default categories named in the chosen language) and writes the
/// <c>language</c>, <c>theme</c> and <c>last_profile_id</c> settings. Quitting before that leaves the database untouched, so setup shows again.
/// </summary>
public sealed partial class SetupViewModel : ObservableObject
{
    public const int StepCount = 5;

    private readonly ProfileRepo _profiles;
    private readonly Action<string>? _previewLanguage;
    private readonly Action<AppTheme>? _previewTheme;
    private Profile? _created;
    private readonly string _whatsNewVersion = WhatsNewService.CurrentVersion;

    /// <param name="profiles">Where the profile is created.</param>
    /// <param name="initialLanguage">The language the app is showing now (the Windows default).</param>
    /// <param name="initialTheme">The theme the app is showing now.</param>
    /// <param name="previewLanguage">Shows a language live without saving it.</param>
    /// <param name="previewTheme">Shows a theme live without saving it.</param>
    public SetupViewModel(ProfileRepo profiles, string initialLanguage, AppTheme initialTheme,
        Action<string>? previewLanguage = null, Action<AppTheme>? previewTheme = null)
    {
        _profiles = profiles;
        _previewLanguage = previewLanguage;
        _previewTheme = previewTheme;
        _languageCode = LocalizationService.Find(initialLanguage)?.Code ?? LocalizationService.EnglishCode;
        _theme = initialTheme;
        _color = ProfileEditViewModel.Palette[0];
        _filteredLanguages = SetupLanguages;
        for (var i = 0; i < StepCount; i++) Dots.Add(new StepDot(i, StepCount));
        UpdateDots();
    }

    /// <summary>Setup only runs on a database with no profiles: an existing user never sees it.</summary>
    public static bool ShouldRun(ProfileRepo profiles) => profiles.Count() == 0;

    /// <summary>The languages offered by setup: the 32 real ones (never the debug pseudo-locale).</summary>
    public static IReadOnlyList<LanguageInfo> SetupLanguages { get; } =
        LocalizationService.Supported.Where(l => l.Code != LocalizationService.PseudoCode).ToList();

    public ObservableCollection<StepDot> Dots { get; } = new();

    // ---- steps --------------------------------------------------------------------------------------

    [ObservableProperty, NotifyPropertyChangedFor(nameof(StepIndex), nameof(CanBack), nameof(CanNext), nameof(CanSkip), nameof(IsDone), nameof(NextIsVisible))]
    private SetupStep _step = SetupStep.Language;

    /// <summary>0-based index of the current step (for the dots).</summary>
    public int StepIndex => (int)Step;

    public int Count => StepCount;

    public bool IsDone => Step == SetupStep.Done;

    /// <summary>Back is hidden on the first page and after the profile has been created.</summary>
    public bool CanBack => Step != SetupStep.Language && _created is null;

    public bool CanSkip => Step == SetupStep.Pin;

    /// <summary>The Next button exists on the first four pages (the last page has its own two buttons).</summary>
    public bool NextIsVisible => Step != SetupStep.Done;

    /// <summary>Whether Next may be used: the Profile page needs a name, the PIN page a complete matching PIN (or none at all).</summary>
    public bool CanNext => Step switch
    {
        SetupStep.Language => true,
        SetupStep.Appearance => true,
        SetupStep.Profile => IsNameValid,
        SetupStep.Pin => PinIsEmpty || HasPin,
        _ => false,
    };

    public bool Next()
    {
        if (!CanNext || Step == SetupStep.Done) return false;
        Step = (SetupStep)((int)Step + 1);
        return true;
    }

    public bool Back()
    {
        if (!CanBack) return false;
        Step = (SetupStep)((int)Step - 1);
        return true;
    }

    /// <summary>Skips the PIN page: no PIN is set (anything typed so far is dropped).</summary>
    public bool Skip()
    {
        if (!CanSkip) return false;
        Pin = "";
        PinConfirm = "";
        Step = SetupStep.Done;
        return true;
    }

    partial void OnStepChanged(SetupStep value) => UpdateDots();

    private void UpdateDots()
    {
        for (var i = 0; i < Dots.Count; i++)
        {
            Dots[i].IsCurrent = i == StepIndex;
            Dots[i].IsDone = i < StepIndex;
        }
    }

    // ---- language -----------------------------------------------------------------------------------

    [ObservableProperty] private string _languageCode;

    partial void OnLanguageCodeChanged(string value)
    {
        if (LocalizationService.Find(value) is not null) _previewLanguage?.Invoke(value);
        OnPropertyChanged(nameof(LanguageNativeName));
    }

    public string LanguageNativeName => LocalizationService.Find(LanguageCode)?.NativeName ?? LanguageCode;

    [ObservableProperty] private string _languageSearch = "";

    [ObservableProperty] private IReadOnlyList<LanguageInfo> _filteredLanguages;

    partial void OnLanguageSearchChanged(string value) => FilteredLanguages = FilterLanguages(value, SetupLanguages);

    /// <summary>
    /// Languages whose native name, English name or code contains the search text (case and accents ignored: "espanol" finds Español,
    /// "deutsch" and "german" both find German). An empty search lists everything.
    /// </summary>
    public static IReadOnlyList<LanguageInfo> FilterLanguages(string? search, IEnumerable<LanguageInfo> all)
    {
        var q = Fold(search);
        if (q.Length == 0) return all.ToList();
        return all.Where(l => Fold(l.NativeName).Contains(q) || Fold(l.EnglishName).Contains(q) || Fold(l.Code).Contains(q)).ToList();
    }

    private static string Fold(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var sb = new StringBuilder();
        foreach (var c in text.Trim().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }

    // ---- appearance ---------------------------------------------------------------------------------

    [ObservableProperty] private AppTheme _theme;

    partial void OnThemeChanged(AppTheme value) => _previewTheme?.Invoke(value);

    public string ThemeKey => "Settings_Theme_" + Theme;

    // ---- profile ------------------------------------------------------------------------------------

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanNext), nameof(Initial), nameof(IsNameValid), nameof(DisplayName))]
    private string _name = "";

    [ObservableProperty] private string _color;

    /// <summary>'builtin:fox' or null for the coloured initial.</summary>
    [ObservableProperty] private string? _avatar;

    public bool IsNameValid => Name.Trim().Length is > 0 and <= ProfileEditViewModel.MaxNameLength;

    public string DisplayName => Name.Trim();

    public string Initial => ProfileEditViewModel.DeriveInitial(Name);

    // ---- PIN ----------------------------------------------------------------------------------------

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanNext), nameof(HasPin), nameof(PinIsEmpty))]
    private string _pin = "";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanNext), nameof(HasPin), nameof(PinIsEmpty))]
    private string _pinConfirm = "";

    public bool PinIsEmpty => Pin.Length == 0 && PinConfirm.Length == 0;

    /// <summary>True when a complete PIN was typed twice the same way. This is what <see cref="Complete"/> stores.</summary>
    public bool HasPin => PinHasher.IsValidPin(Pin) && Pin == PinConfirm;

    // ---- finish -------------------------------------------------------------------------------------

    /// <summary>The profile created by <see cref="Complete"/>, or null before that.</summary>
    public Profile? Created => _created;

    /// <summary>
    /// Creates the profile and saves the choices, all in one transaction. Calling it again returns the same profile.
    /// Throws if the name is blank (the Profile page does not let Next through without one).
    /// </summary>
    public Profile Complete()
    {
        if (_created is not null) return _created;
        if (!IsNameValid) throw new InvalidOperationException("A profile needs a name.");

        var culture = CultureInfo.GetCultureInfo(LanguageCode);
        var categories = new[]
        {
            Loc.Get("Category_School", culture),
            Loc.Get("Category_Work", culture),
            Loc.Get("Category_Personal", culture),
        };
        var settings = new Dictionary<string, string>
        {
            [LocalizationService.SettingKey] = LanguageCode,
            [ThemeService.SettingKey] = Theme.ToString(),
            // A new user starts "up to date": the What's New page is for people who update.
            [WhatsNewService.LastSeenKey] = _whatsNewVersion,
        };
        _created = _profiles.CreateFirst(DisplayName, Color, ProfileEditViewModel.DeriveInitial(Name),
            HasPin ? Pin : null, Avatar, categories, settings);
        OnPropertyChanged(nameof(Created));
        OnPropertyChanged(nameof(CanBack));
        return _created;
    }
}
