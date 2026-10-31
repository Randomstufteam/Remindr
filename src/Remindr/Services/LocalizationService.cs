using System.Globalization;
using Remindr.Data.Repositories;

namespace Remindr.Services;

/// <summary>One language the app can be shown in. Names are written in the language itself (native) and in English.</summary>
/// <param name="Reviewed">True when a person has checked the translation. False means it is shown with a "Machine-translated" tag.</param>
public sealed record LanguageInfo(string Code, string NativeName, string EnglishName, bool IsRtl, bool Reviewed = false)
{
    /// <summary>The English name in brackets as secondary text beside the native name; empty when both are the same word.</summary>
    public string EnglishLabel => string.Equals(NativeName, EnglishName, StringComparison.Ordinal) ? "" : "(" + EnglishName + ")";

    /// <summary>True for languages without a native-speaker review: the picker tags them "Machine-translated". English is the source text and never tagged.</summary>
    public bool IsMachineTranslated => !Reviewed;

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

    private static readonly LanguageInfo[] Languages = LanguageCatalog.All();

    /// <summary>
    /// Every language in the catalog (176, English first, plus the pseudo-locale in Debug builds). Some have no text yet: they fall
    /// back to English until their translation batch lands, so the pickers list <see cref="Available"/> instead.
    /// </summary>
    public static IReadOnlyList<LanguageInfo> Supported { get; } = BuildSupported();

    /// <summary>The languages that can be chosen now: the catalog entries whose translation is shipped (English and every <c>Strings.&lt;code&gt;.resx</c>).</summary>
    public static IReadOnlyList<LanguageInfo> Available => _available ??= Supported.Where(l => IsTranslated(l.Code)).ToList();
    private static IReadOnlyList<LanguageInfo>? _available;

    /// <summary>True for English, the Debug pseudo-locale, and any language whose string table is shipped with the app.</summary>
    public static bool IsTranslated(string? code)
    {
        if (code is null) return false;
        if (string.Equals(code, EnglishCode, StringComparison.OrdinalIgnoreCase) || string.Equals(code, PseudoCode, StringComparison.OrdinalIgnoreCase)) return true;
        if (Find(code) is not { } info) return false;
        return Translated.GetOrAdd(info.Code, c => Loc.HasTranslation(c));
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> Translated = new(StringComparer.Ordinal);


    private static IReadOnlyList<LanguageInfo> BuildSupported()
    {
        var list = new List<LanguageInfo>(Languages);
#if DEBUG
        list.Add(new LanguageInfo(PseudoCode, "[Þšéůḋó] (debug)", "Pseudo-locale (debug)", false, true));
#endif
        return list;
    }

    public static LanguageInfo? Find(string? code) =>
        code is null ? null : Supported.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Maps a Windows / .NET culture name to a catalog language code, or null. Region and script variants walk up to their language
    /// (es-MX to es, de-AT to de, sr-Latn-RS to sr-Latn, pa-Guru-IN to pa, pa-Arab-PK to pa-Arab, fa-AF to fa); Chinese splits by
    /// script/region (zh-CN, zh-Hans-* to zh-Hans; zh-TW, zh-HK, zh-MO, zh-Hant-* to zh-Hant); plain pt is Brazilian; a language the
    /// catalog lists only per script maps to its usual script (sr to sr-Cyrl, uz to uz-Latn); and a few legacy codes are renamed
    /// (no to nb, iw to he, in to id). The result may not be translated yet: see <see cref="IsTranslated"/>.
    /// </summary>
    public static string? ResolveSupported(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName)) return null;
        var name = cultureName.Trim().Replace('_', '-');
        var parts = name.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        var lang = parts[0].ToLowerInvariant();
        var rest = parts.Skip(1).Select(p => p.ToLowerInvariant()).ToList();
        var script = rest.FirstOrDefault(p => p.Length == 4 && p.All(char.IsLetter));
        var region = rest.FirstOrDefault(p => p.Length == 2 && p.All(char.IsLetter) || p.Length == 3 && p.All(char.IsDigit));

        switch (lang)
        {
            case "zh":
                return rest.Contains("hant") || rest.Any(p => p is "tw" or "hk" or "mo") ? "zh-Hant" : "zh-Hans";
            case "pt":
                return rest.Contains("pt") ? "pt-PT" : "pt-BR";
            case "no": case "nb":
                return "nb";
            case "tl": case "fil":
                return "fil";
            case "iw":
                return "he";
            case "in":
                return "id";
            case "ji":
                return "yi";
            case "jw":
                return "jv";
            case "ku" when script == "arab":
                return "ckb";
            case "pa" when script is null && region == "pk":
                return "pa-Arab";
        }

        if (script is not null)
        {
            // Language and script together first (sr-Latn, az-Latn), then the bare language (pa-Guru is pa). A script the catalog does
            // not have (uz-Cyrl) is not mapped to another script's text.
            var withScript = FindExact(lang + "-" + script);
            if (withScript is not null) return withScript.Code;
            return FindExact(lang)?.Code;
        }

        if (FindExact(lang) is { } exact) return exact.Code;
        // The catalog has this language only per script: use the usual one.
        return lang switch
        {
            "az" => "az-Latn",
            "bs" => "bs-Latn",
            "uz" => "uz-Latn",
            "ff" => "ff-Latn",
            "sr" => "sr-Cyrl",
            "sd" => "sd-Arab",
            "ks" => "ks-Arab",
            "mni" => "mni-Beng",
            "sat" => "sat-Olck",
            _ => null,
        };
    }

    private static LanguageInfo? FindExact(string code) =>
        Languages.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>When a language has no text yet, the closest shipped relative is preferred over English (Nynorsk shows Bokmål).</summary>
    private static readonly Dictionary<string, string> Relatives = new(StringComparer.Ordinal)
    {
        ["nn"] = "nb",
    };

    /// <summary>The language to start with: a saved choice that is shipped wins, else the Windows display language when it is shipped (or a close relative is), else English.</summary>
    public static string ResolveDefault(string? saved, CultureInfo? windowsUi)
    {
        if (Find(saved) is { } s && IsTranslated(s.Code)) return s.Code;
        var resolved = ResolveSupported(windowsUi?.Name);
        if (resolved is null) return EnglishCode;
        if (IsTranslated(resolved)) return resolved;
        return Relatives.TryGetValue(resolved, out var relative) && IsTranslated(relative) ? relative : EnglishCode;
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
        var resourceCulture = CultureFor(pseudo ? EnglishCode : info.Code);
        Code = info.Code;
        Current = ForceGregorian(FormatCultureFor(resourceCulture));
        if (!_applyToProcess) return;

        CultureInfo.DefaultThreadCurrentCulture = Current;
        CultureInfo.DefaultThreadCurrentUICulture = resourceCulture;
        CultureInfo.CurrentCulture = Current;
        CultureInfo.CurrentUICulture = resourceCulture;
        Loc.Apply(resourceCulture, Current, pseudo);
    }

    /// <summary>The culture of a catalog code; English when this Windows cannot create it (a test checks the catalog on current Windows).</summary>
    public static CultureInfo CultureFor(string code)
    {
        try { return CultureInfo.GetCultureInfo(code, predefinedOnly: true); }
        catch (CultureNotFoundException) { return CultureInfo.GetCultureInfo(EnglishCode); }
    }

    /// <summary>The regional culture when it is the same language (en-GB, de-AT), otherwise the language's own culture.</summary>
    private CultureInfo FormatCultureFor(CultureInfo language)
    {
        var sameLanguage = ResolveSupported(_regional.Name) is { } r && string.Equals(r, ResolveSupported(language.Name), StringComparison.Ordinal);
        return sameLanguage && !_regional.IsNeutralCulture ? _regional : language;
    }

    /// <summary>
    /// The same culture with the Gregorian calendar, for display. Some languages default to another calendar (fa: Persian, th: Buddhist,
    /// ps and dz can differ too), which would change the month grid and the year in every date. Stored data is always invariant. A culture
    /// that is already Gregorian, or has no Gregorian option, is returned unchanged.
    /// </summary>
    public static CultureInfo ForceGregorian(CultureInfo culture)
    {
        try
        {
            if (culture.DateTimeFormat.Calendar is GregorianCalendar) return culture;
            if (!culture.OptionalCalendars.Any(c => c is GregorianCalendar)) return culture;
            var clone = (CultureInfo)culture.Clone();
            clone.DateTimeFormat.Calendar = culture.OptionalCalendars.First(c => c is GregorianCalendar);
            return clone;
        }
        catch (ArgumentException)
        {
            return culture;
        }
    }
}
