using System.Globalization;
using System.Text;

namespace Remindr.Services;

/// <summary>
/// Search and ordering for the language picker (UI-free). A query matches the native name, the English name or the code, ignoring case
/// and accents ("espanol" finds Español, "portugu" finds both Portuguese). Names starting with the query come before names that only
/// contain it. With no query the list is alphabetical by English name with the pinned language (the current one, or the Windows
/// display language in setup) first.
/// </summary>
public static class LanguageSearch
{
    /// <summary>Lower case, accents removed (NFD, no combining marks), a few look-alike letters unified.</summary>
    public static string Fold(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var sb = new StringBuilder();
        foreach (var c in text.Trim().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            // Ethiopic: the Tigrinya-style "a" (U+12A3) and the Amharic letter it is typed for (U+12A0) are the same search for a reader.
            sb.Append(c == 'ኣ' ? 'አ' : c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }

    /// <summary>The languages matching the search, best matches first; everything (in the given order) for an empty search.</summary>
    public static IReadOnlyList<LanguageInfo> Filter(string? search, IEnumerable<LanguageInfo> all)
    {
        var q = Fold(search);
        if (q.Length == 0) return all.ToList();
        var scored = new List<(LanguageInfo Info, int Score, int Order)>();
        var order = 0;
        foreach (var l in all)
        {
            var score = Score(l, q);
            if (score >= 0) scored.Add((l, score, order));
            order++;
        }
        return scored.OrderBy(s => s.Score).ThenBy(s => s.Order).Select(s => s.Info).ToList();
    }

    /// <summary>-1 for no match; 0 exact code or a name that starts with the query; 1 a word inside a name starts with it; 2 a name contains it.</summary>
    private static int Score(LanguageInfo l, string q)
    {
        var native = Fold(l.NativeName);
        var english = Fold(l.EnglishName);
        var code = Fold(l.Code);
        if (code == q || native.StartsWith(q, StringComparison.Ordinal) || english.StartsWith(q, StringComparison.Ordinal)) return 0;
        if (StartsAWord(native, q) || StartsAWord(english, q) || (q.Length >= 2 && code.StartsWith(q, StringComparison.Ordinal))) return 1;
        if (native.Contains(q, StringComparison.Ordinal) || english.Contains(q, StringComparison.Ordinal)) return 2;
        return -1;
    }

    private static bool StartsAWord(string text, string q)
    {
        for (var i = text.IndexOf(q, StringComparison.Ordinal); i >= 0; i = text.IndexOf(q, i + 1, StringComparison.Ordinal))
            if (i == 0 || !char.IsLetterOrDigit(text[i - 1])) return true;
        return false;
    }

    /// <summary>
    /// The browsing order: the pinned language first (when it is in the list), then English by name. Used when there is no search; a search
    /// keeps its own best-match order.
    /// </summary>
    public static IReadOnlyList<LanguageInfo> Arrange(IEnumerable<LanguageInfo> languages, string? pinnedCode)
    {
        var rest = languages.OrderBy(l => l.EnglishName, StringComparer.OrdinalIgnoreCase).ToList();
        var pinned = pinnedCode is null ? null : rest.FirstOrDefault(l => string.Equals(l.Code, pinnedCode, StringComparison.OrdinalIgnoreCase));
        if (pinned is null) return rest;
        rest.Remove(pinned);
        rest.Insert(0, pinned);
        return rest;
    }
}
