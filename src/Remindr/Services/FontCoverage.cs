using System.Windows;
using System.Windows.Media;

namespace Remindr.Services;

/// <summary>
/// Checks which characters of a text none of a language's fallback fonts can draw (they would show as empty boxes, "tofu").
/// Used by the Debug-only <c>--font-check</c> page and by a test that runs it for every language.
/// </summary>
public static class FontCoverage
{
    /// <summary>The characters of <paramref name="text"/> that no font of <see cref="FontPolicy.Chain"/> has a glyph for (control and combining-format characters are ignored).</summary>
    public static IReadOnlyList<string> Missing(string baseFont, string languageCode, string text)
    {
        var glyphs = new List<GlyphTypeface>();
        foreach (var name in FontPolicy.Chain(baseFont, languageCode).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                if (new Typeface(new FontFamily(name), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal).TryGetGlyphTypeface(out var g)) glyphs.Add(g);
            }
            catch (Exception ex) when (ex is ArgumentException or System.IO.IOException or UnauthorizedAccessException) { /* a font that is not installed */ }
        }

        var missing = new List<string>();
        for (var i = 0; i < text.Length; i++)
        {
            var cp = char.ConvertToUtf32(text, i);
            if (char.IsHighSurrogate(text[i])) i++;
            var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(cp);
            if (category is System.Globalization.UnicodeCategory.Control or System.Globalization.UnicodeCategory.Format
                || char.IsWhiteSpace(char.ConvertFromUtf32(cp), 0)) continue;
            if (!glyphs.Any(g => g.CharacterToGlyphMap.ContainsKey(cp))) missing.Add(char.ConvertFromUtf32(cp) + " (U+" + cp.ToString("X4") + ")");
        }
        return missing;
    }
}
