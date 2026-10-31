namespace Remindr.Services;

/// <summary>
/// Plural category ("One", "Two", "Few", "Many", "Other", "Zero") of a whole number for a language, from the Unicode CLDR cardinal
/// rules. The rules are generated (<c>PluralRules.Generated.cs</c>, by <c>tools/gen-plurals.ps1</c> from a pinned copy of CLDR's
/// plurals.xml), never typed by hand. <see cref="Loc.Plural"/> maps the category to a resource key suffix and falls back to "Other"
/// when a language has no key for it. A language CLDR has no rule for (and the one-category languages) is always "Other".
/// </summary>
public static partial class PluralRules
{
    public static string Category(string language, long n)
    {
        n = n == long.MinValue ? long.MaxValue : Math.Abs(n);
        var parts = language.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "Other";
        var lang = parts[0].ToLowerInvariant();
        // A region-specific rule first (pt-PT), then the language.
        if (parts.Length > 1 && parts[1].Length == 2 && Generated(lang + "_" + parts[1].ToUpperInvariant(), n) is { } regional) return regional;
        return Generated(lang, n) ?? "Other";
    }

    /// <summary>The categories a language can produce for the whole numbers 0 to 1000 (a test uses it to require a text for each).</summary>
    public static IReadOnlyCollection<string> CategoriesFor(string language) =>
        Enumerable.Range(0, 1001).Select(i => Category(language, i)).Distinct().ToList();
}
