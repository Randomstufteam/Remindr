using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace Remindr.Services;

/// <summary>
/// The one place user-facing text comes from: <c>Resources\Strings.resx</c> (neutral English) plus optional
/// <c>Strings.&lt;culture&gt;.resx</c> satellites, which the ResourceManager falls back through (es-MX to es to English).
/// <see cref="Instance"/> is the binding source for <c>{l:Loc Key}</c>: its indexer raises <c>Item[]</c> when the language changes,
/// so every bound string refreshes with no restart. UI-free (no WPF types), so the logic ports and unit tests need no window.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    private static readonly ResourceManager Resources = new("Remindr.Resources.Strings", typeof(Loc).Assembly);

    /// <summary>Culture the strings are read in (a supported language, e.g. pt-BR). English until a language is applied.</summary>
    public static CultureInfo ResourceCulture { get; private set; } = CultureInfo.GetCultureInfo("en");

    /// <summary>Culture used for dates, times, numbers and the first day of the week.</summary>
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en");

    /// <summary>Debug-only pseudo-locale (qps-ploc): English text is accented and padded to expose unwrapped strings and expansion.</summary>
    public static bool IsPseudo { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after the language changed. View models that build strings themselves recompute on it.</summary>
    public static event EventHandler? CultureChanged;

    /// <summary>The string for <paramref name="key"/> in the current language. Used by bindings.</summary>
    public string this[string key] => Get(key);

    public static string Get(string key) => Get(key, ResourceCulture, IsPseudo);

    /// <summary>A missing key shows as !key! so it is obvious on screen (a test also checks every key used exists).</summary>
    public static string Get(string key, CultureInfo culture, bool pseudo = false)
    {
        var text = Resources.GetString(key, culture);
        if (text is null) return "!" + key + "!";
        // Fmt_* values are date/time format patterns, not text: they must stay valid patterns.
        return pseudo && !key.StartsWith("Fmt_", StringComparison.Ordinal) ? Pseudo.Transform(text) : text;
    }

    /// <summary>Looks the string up and formats it (string.Format with the current culture).</summary>
    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

    /// <summary>
    /// Count-dependent text: reads <c>key_Zero</c> when n is 0 and that key exists, otherwise the key for the language's plural
    /// category (<c>key_One</c>, <c>key_Few</c>, ...), falling back to <c>key_Other</c>. {0} in the text is the count.
    /// </summary>
    public static string Plural(string key, long n)
    {
        if (n == 0 && Resources.GetString(key + "_Zero", ResourceCulture) is not null) return Format(key + "_Zero", n);
        var category = PluralRules.Category(ResourceCulture.Name, n);
        var specific = Resources.GetString(key + "_" + category, ResourceCulture);
        return Format(specific is not null ? key + "_" + category : key + "_Other", n);
    }

    /// <summary>True when the neutral file has this key (used by tests).</summary>
    public static bool Has(string key) => Resources.GetString(key, CultureInfo.InvariantCulture) is not null;

    /// <summary>Applies a language. Called by <see cref="LocalizationService"/>; raises the change notifications.</summary>
    internal static void Apply(CultureInfo resourceCulture, CultureInfo formatCulture, bool pseudo)
    {
        ResourceCulture = resourceCulture;
        Culture = formatCulture;
        IsPseudo = pseudo;
        Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs("Item[]"));
        CultureChanged?.Invoke(Instance, EventArgs.Empty);
    }
}
