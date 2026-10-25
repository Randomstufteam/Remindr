namespace Remindr.Services;

/// <summary>
/// Plural category ("One", "Two", "Few", "Many", "Other", "Zero") of a whole number for a language, after the CLDR rules for the 32
/// supported languages. <see cref="Loc.Plural"/> maps it to a resource key suffix and falls back to "Other" when a language has
/// no key for the category.
/// </summary>
public static class PluralRules
{
    public static string Category(string language, long n)
    {
        n = Math.Abs(n);
        var lang = language.Split('-')[0].ToLowerInvariant();
        var m10 = n % 10;
        var m100 = n % 100;
        switch (lang)
        {
            case "ja": case "ko": case "zh": case "th": case "vi": case "id":
                return "Other";
            case "fr": case "hi": case "bn":
                return n <= 1 ? "One" : "Other";
            case "pt":
                // pt-PT: only 1 is singular; pt-BR (and plain pt): 0 and 1.
                return language.StartsWith("pt-PT", StringComparison.OrdinalIgnoreCase)
                    ? (n == 1 ? "One" : "Other")
                    : (n <= 1 ? "One" : "Other");
            case "ru": case "uk":
                if (m10 == 1 && m100 != 11) return "One";
                if (m10 is >= 2 and <= 4 && m100 is not (>= 12 and <= 14)) return "Few";
                return "Many";
            case "pl":
                if (n == 1) return "One";
                if (m10 is >= 2 and <= 4 && m100 is not (>= 12 and <= 14)) return "Few";
                return "Many";
            case "cs":
                if (n == 1) return "One";
                return n is >= 2 and <= 4 ? "Few" : "Other";
            case "ro":
                if (n == 1) return "One";
                return n == 0 || m100 is >= 2 and <= 19 ? "Few" : "Other";
            case "ar":
                if (n == 0) return "Zero";
                if (n == 1) return "One";
                if (n == 2) return "Two";
                if (m100 is >= 3 and <= 10) return "Few";
                return m100 >= 11 ? "Many" : "Other";
            case "he":
                return n == 1 ? "One" : n == 2 ? "Two" : "Other";
            default:
                // en es de it nl sv nb da fi el tr hu fil: one / other.
                return n == 1 ? "One" : "Other";
        }
    }
}
