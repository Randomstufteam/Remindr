using System.Globalization;
using Remindr.Data.Repositories;

namespace Remindr.Services;

/// <summary>One language the app can be shown in. Names are written in the language itself (native) and in English.</summary>
public sealed record LanguageInfo(string Code, string NativeName, string EnglishName, bool IsRtl)
{
    /// <summary>The English name in brackets as secondary text beside the native name; empty when both are the same word.</summary>
    public string EnglishLabel => string.Equals(NativeName, EnglishName, StringComparison.Ordinal) ? "" : "(" + EnglishName + ")";

    /// <summary>What a screen reader says for a list entry: "Deutsch (German)". (The default record text read as junk.)</summary>
    public override string ToString() => EnglishLabel.Length == 0 ? NativeName : NativeName + " " + EnglishLabel;
}

/// <summary>
/// Which language the app uses. UI-free: it decides, remembers (global app_settings key <c>language</c>) and announces; the strings
/// themselves come from <see cref="Loc"/>. Default: the saved choice, else the Windows display language when supported, else English.
/// </summary>
public sealed class LocalizationService
{
    public const string SettingKey = "language";
    public const string EnglishCode = "en";

    /// <summary>Debug builds only: accented, padded English that exposes hard-coded text and layout expansion.</summary>
    public const string PseudoCode = "qps-ploc";

    private static readonly LanguageInfo[] Languages =
    {
        new("en", "English", "English", false),
        new("es", "Español", "Spanish", false),
        new("fr", "Français", "French", false),
        new("de", "Deutsch", "German", false),
        new("it", "Italiano", "Italian", false),
        new("pt-BR", "Português (Brasil)", "Portuguese (Brazil)", false),
        new("pt-PT", "Português (Portugal)", "Portuguese (Portugal)", false),
        new("nl", "Nederlands", "Dutch", false),
        new("pl", "Polski", "Polish", false),
        new("sv", "Svenska", "Swedish", false),
        new("nb", "Norsk bokmål", "Norwegian Bokmål", false),
        new("da", "Dansk", "Danish", false),
        new("fi", "Suomi", "Finnish", false),
        new("cs", "Čeština", "Czech", false),
        new("hu", "Magyar", "Hungarian", false),
        new("ro", "Română", "Romanian", false),
        new("el", "Ελληνικά", "Greek", false),
        new("tr", "Türkçe", "Turkish", false),
        new("ru", "Русский", "Russian", false),
        new("uk", "Українська", "Ukrainian", false),
        new("ar", "العربية", "Arabic", true),
        new("he", "עברית", "Hebrew", true),
        new("hi", "हिन्दी", "Hindi", false),
        new("bn", "বাংলা", "Bengali", false),
        new("id", "Bahasa Indonesia", "Indonesian", false),
        new("vi", "Tiếng Việt", "Vietnamese", false),
        new("th", "ไทย", "Thai", false),
        new("ja", "日本語", "Japanese", false),
        new("ko", "한국어", "Korean", false),
        new("zh-Hans", "简体中文", "Chinese (Simplified)", false),
        new("zh-Hant", "繁體中文", "Chinese (Traditional)", false),
        new("fil", "Filipino", "Filipino", false),
    };

    /// <summary>All 32 languages (plus the pseudo-locale in Debug builds), English first.</summary>
    public static IReadOnlyList<LanguageInfo> Supported { get; } = BuildSupported();

    private static IReadOnlyList<LanguageInfo> BuildSupported()
    {
        var list = new List<LanguageInfo>(Languages);
#if DEBUG
        list.Add(new LanguageInfo(PseudoCode, "[Þšéůḋó] (debug)", "Pseudo-locale (debug)", false));
#endif
        return list;
    }

    public static LanguageInfo? Find(string? code) =>
        code is null ? null : Supported.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Maps a Windows / .NET culture name to a supported language code, or null. Region and script variants walk up to their language
    /// (es-MX to es, de-AT to de), Chinese splits by script/region (zh-CN, zh-Hans-* to zh-Hans; zh-TW, zh-HK, zh-MO, zh-Hant-* to
    /// zh-Hant), plain pt is Brazilian, and a few legacy codes are renamed (no, nn to nb).
    /// </summary>
    public static string? ResolveSupported(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName)) return null;
        var name = cultureName.Trim().Replace('_', '-');
        var parts = name.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        var lang = parts[0].ToLowerInvariant();
        var rest = parts.Skip(1).Select(p => p.ToLowerInvariant()).ToList();

        switch (lang)
        {
            case "zh":
                return rest.Contains("hant") || rest.Any(p => p is "tw" or "hk" or "mo") ? "zh-Hant" : "zh-Hans";
            case "pt":
                return rest.Contains("pt") ? "pt-PT" : "pt-BR";
            case "no": case "nn": case "nb":
                return "nb";
            case "tl": case "fil":
                return "fil";
            case "iw":
                return "he";
            case "in":
                return "id";
        }
        var match = Languages.FirstOrDefault(l => string.Equals(l.Code, lang, StringComparison.OrdinalIgnoreCase));
        return match?.Code;
    }

    /// <summary>The language to start with: a saved supported choice wins, else the Windows display language, else English.</summary>
    public static string ResolveDefault(string? saved, CultureInfo? windowsUi)
    {
        if (Find(saved) is { } s) return s.Code;
        return ResolveSupported(windowsUi?.Name) ?? EnglishCode;
    }

    private readonly SettingsRepo _settings;
    private readonly CultureInfo _windowsUi;
    private readonly CultureInfo _regional;
    private readonly bool _applyToProcess;

    /// <param name="applyToProcess">
    /// Also set the thread cultures and the strings behind <see cref="Loc"/> (the app). Tests pass false so they cannot disturb
    /// each other through process-wide state.
    /// </param>
    /// <param name="windowsUi">The Windows display language (defaults to <see cref="CultureInfo.InstalledUICulture"/>).</param>
    /// <param name="regional">The Windows regional format culture (defaults to the culture at start-up).</param>
    public LocalizationService(SettingsRepo settings, bool applyToProcess = true, CultureInfo? windowsUi = null, CultureInfo? regional = null)
    {
        _settings = settings;
        _applyToProcess = applyToProcess;
        _windowsUi = windowsUi ?? CultureInfo.InstalledUICulture;
        _regional = regional ?? CultureInfo.CurrentCulture;
        Code = EnglishCode;
        Current = CultureInfo.GetCultureInfo(EnglishCode);
    }

    /// <summary>The selected language code (a <see cref="Supported"/> entry).</summary>
    public string Code { get; private set; }

    /// <summary>Culture for dates, times and numbers: the Windows regional format when it speaks the chosen language (en-GB for an English UI), else the language itself.</summary>
    public CultureInfo Current { get; private set; }

    public LanguageInfo Language => Find(Code) ?? Languages[0];

    public bool IsRtl => Language.IsRtl;

    /// <summary>Raised after <see cref="SetLanguage"/> changed the language.</summary>
    public event EventHandler? CultureChanged;

    /// <summary>Loads the saved (or default) language and applies it. Nothing is written. Call once at start-up.</summary>
    public void Initialize() => Apply(ResolveDefault(_settings.Get(SettingKey), _windowsUi));

    /// <summary>Switches language now, remembers it, and raises <see cref="CultureChanged"/>. Unsupported codes are ignored.</summary>
    public void SetLanguage(string code)
    {
        if (Find(code) is not { } info) return;
        _settings.Set(SettingKey, info.Code);
        if (string.Equals(info.Code, Code, StringComparison.Ordinal)) return;
        Apply(info.Code);
        CultureChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Shows a language now without remembering it (first-run setup previews the choice live and only saves it when setup is finished).
    /// Unsupported codes are ignored. Raises <see cref="CultureChanged"/> when the language actually changed.
    /// </summary>
    public void PreviewLanguage(string code)
    {
        if (Find(code) is not { } info || string.Equals(info.Code, Code, StringComparison.Ordinal)) return;
        Apply(info.Code);
        CultureChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Apply(string code)
    {
        var info = Find(code) ?? Languages[0];
        var pseudo = info.Code == PseudoCode;
        // Resources are read for the language; pseudo is English text run through Pseudo.
        var resourceCulture = CultureInfo.GetCultureInfo(pseudo ? EnglishCode : info.Code);
        Code = info.Code;
        Current = FormatCultureFor(resourceCulture);
        if (!_applyToProcess) return;

        CultureInfo.DefaultThreadCurrentCulture = Current;
        CultureInfo.DefaultThreadCurrentUICulture = resourceCulture;
        CultureInfo.CurrentCulture = Current;
        CultureInfo.CurrentUICulture = resourceCulture;
        Loc.Apply(resourceCulture, Current, pseudo);
    }

    /// <summary>The regional culture when it is the same language (en-GB, de-AT), otherwise the language's own culture.</summary>
    private CultureInfo FormatCultureFor(CultureInfo language)
    {
        var sameLanguage = ResolveSupported(_regional.Name) is { } r && string.Equals(r, ResolveSupported(language.Name), StringComparison.Ordinal);
        return sameLanguage && !_regional.IsNeutralCulture ? _regional : language;
    }
}
